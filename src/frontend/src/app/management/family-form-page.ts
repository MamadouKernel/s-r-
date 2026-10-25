import { Component, inject, OnDestroy, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { ImageService } from '../core/image';
import { ApiError } from '../sync/sync-api';
import { describeError } from './errors';
import { FlashService } from './flash.service';
import { FormatDraft, FormatErrors, newDraft, validateFormats } from './format-draft';
import { FormatFields } from './format-fields';
import { PhotoField } from './photo-field';
import { ProductsApi } from './products-api';

/**
 * Nouveau produit avec ses formats, en un seul écran (docs/specs/formats-produits FMT-03, FMT-07). Tout ou rien : rien n'est enregistré tant qu'on
 * n'appuie pas sur le bouton du bas. La photo est facultative et part après la création.
 */
@Component({
  selector: 'app-family-form-page',
  imports: [RouterLink, FormatFields, PhotoField],
  template: `
    <section class="mx-auto max-w-2xl" aria-labelledby="fiche-titre">
      <a class="btn mb-4" routerLink="/gestion/produits">← Retour aux produits</a>
      <h2 id="fiche-titre" class="mb-4 text-xl font-bold">Nouveau produit</h2>

      <form class="panel" (submit)="save($event)" novalidate>
        <label class="field-label" for="nom">Nom du produit</label>
        <input id="nom" class="field" type="text" maxlength="200" autocomplete="off" [value]="name()" (input)="name.set($any($event.target).value)" [attr.aria-invalid]="nameError() ? 'true' : null" aria-describedby="nom-erreur" />
        <p id="nom-erreur" class="field-error">{{ nameError() }}</p>

        <label class="field-label mt-4" for="categorie">Catégorie (facultatif)</label>
        <input id="categorie" class="field" type="text" maxlength="100" autocomplete="off" [value]="category()" (input)="category.set($any($event.target).value)" />

        <div class="mt-4">
          <span class="field-label">Photo (facultatif)</span>
          <app-photo-field [name]="name() || 'Produit'" [previewUrl]="preview()" [error]="photoError()" (picked)="pickPhoto($event)" (removed)="clearPhoto()" />
        </div>

        <h3 class="mt-6 text-lg font-bold">{{ drafts().length > 1 ? 'Formats' : 'Format' }}</h3>
        <p class="field-help">Un seul format ? Laissez « Format » vide. Dès deux formats, chacun doit avoir le sien (5 kg, 25 kg, 50 kg…).</p>
        <div class="mt-3 flex flex-col gap-3">
          @for (draft of drafts(); track draft.key; let index = $index) {
            <app-format-fields
              [draft]="draft"
              [errors]="errors()[index] ?? {}"
              [idBase]="'f' + index"
              [legend]="'Format ' + (index + 1)"
              [requireLabel]="drafts().length > 1"
              [removable]="drafts().length > 1"
              (patch)="patchDraft(draft.key, $event)"
              (remove)="removeDraft(draft.key)"
            />
          }
        </div>
        @if (formatsError()) {
          <p class="field-error mt-2" role="alert">{{ formatsError() }}</p>
        }
        <button type="button" class="btn mt-3" (click)="addDraft()">+ Ajouter un autre format</button>

        @if (failure()) {
          <p class="field-error mt-4" role="alert">{{ failure() }}</p>
        }
        <div class="mt-6 grid grid-cols-2 gap-3">
          <a class="btn" routerLink="/gestion/produits">Annuler</a>
          <button type="submit" class="btn-primary" [disabled]="saving()">{{ saving() ? 'Enregistrement…' : 'Enregistrer le produit' }}</button>
        </div>
      </form>
    </section>
  `,
})
export class FamilyFormPage implements OnDestroy {
  private readonly api = inject(ProductsApi);
  private readonly router = inject(Router);
  private readonly flash = inject(FlashService);
  private readonly images = inject(ImageService);

  /** Identifiant fixé à l'ouverture : un nouvel essai après une erreur réseau ne peut pas créer deux familles. */
  private readonly familyId = crypto.randomUUID();
  private photo: Blob | null = null;

  protected readonly name = signal('');
  protected readonly category = signal('');
  protected readonly drafts = signal<FormatDraft[]>([newDraft()]);
  protected readonly errors = signal<FormatErrors[]>([]);
  protected readonly nameError = signal('');
  protected readonly formatsError = signal('');
  protected readonly failure = signal('');
  protected readonly saving = signal(false);
  protected readonly preview = signal<string | null>(null);
  protected readonly photoError = signal('');

  ngOnDestroy(): void {
    this.revokePreview();
  }

  protected patchDraft(key: string, patch: Partial<FormatDraft>): void {
    this.drafts.update((drafts) => drafts.map((draft) => (draft.key === key ? { ...draft, ...patch } : draft)));
  }

  protected addDraft(): void {
    this.drafts.update((drafts) => [...drafts, newDraft()]);
  }

  protected removeDraft(key: string): void {
    this.drafts.update((drafts) => (drafts.length > 1 ? drafts.filter((draft) => draft.key !== key) : drafts));
    this.errors.set([]);
  }

  protected async pickPhoto(file: File): Promise<void> {
    this.photoError.set('');
    try {
      this.photo = await this.images.shrink(file);
      this.revokePreview();
      this.preview.set(URL.createObjectURL(this.photo));
    } catch (error) {
      this.photoError.set(error instanceof Error ? error.message : 'Cette image ne peut pas être utilisée.');
    }
  }

  protected clearPhoto(): void {
    this.photo = null;
    this.revokePreview();
    this.preview.set(null);
  }

  protected async save(event: Event): Promise<void> {
    event.preventDefault();
    if (this.saving()) {
      return;
    }
    this.failure.set('');
    this.formatsError.set('');
    const name = this.name().trim();
    this.nameError.set(name === '' ? 'Le nom du produit est obligatoire.' : '');
    const checked = validateFormats(this.drafts());
    this.errors.set(checked.errors);
    this.formatsError.set(checked.general);
    if (name === '' || !checked.inputs) {
      return;
    }

    this.saving.set(true);
    try {
      await this.api.createFamily({ familyId: this.familyId, name, category: this.category().trim() || null, formats: checked.inputs });
    } catch (error) {
      this.failure.set(
        error instanceof ApiError && error.code === 'barcode_taken'
          ? 'Un de ces codes-barres est déjà utilisé par un autre produit.'
          : describeError(error),
      );
      this.saving.set(false);
      return;
    }

    let message = 'Produit créé.';
    if (this.photo) {
      try {
        await this.api.setPhoto('family', this.familyId, this.photo);
      } catch {
        message = 'Produit créé, mais la photo n’a pas pu être envoyée. Vous pourrez l’ajouter depuis la fiche du produit.';
      }
    }
    this.flash.show(message);
    this.saving.set(false);
    await this.router.navigateByUrl('/gestion/produits');
  }

  private revokePreview(): void {
    const current = this.preview();
    if (current) {
      URL.revokeObjectURL(current);
    }
  }
}
