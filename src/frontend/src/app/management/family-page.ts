import { Component, computed, inject, OnInit, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { ImageService } from '../core/image';
import { formatQuantity, perUnit } from '../core/measure';
import { formatFcfa } from '../core/money';
import { ApiError } from '../sync/sync-api';
import { describeError } from './errors';
import { FlashService } from './flash.service';
import { FormatDraft, FormatErrors, newDraft, validateFormats } from './format-draft';
import { FormatFields } from './format-fields';
import { PhotoField } from './photo-field';
import { ManagedProduct, ProductsApi } from './products-api';

/**
 * Fiche d'une famille (docs/specs/formats-produits FMT-07) : ce que les formats ont en commun (nom, catégorie, photo), puis la liste des formats
 * avec « Ajouter un format ». Écran de gestion : en ligne seulement.
 */
@Component({
  selector: 'app-family-page',
  imports: [RouterLink, PhotoField, FormatFields],
  template: `
    <section class="mx-auto max-w-2xl" aria-labelledby="famille-titre">
      <a class="btn mb-4" routerLink="/gestion/produits">← Retour aux produits</a>

      @if (loading()) {
        <p class="panel text-ardoise" role="status">Chargement du produit…</p>
      } @else if (loadError()) {
        <p class="panel font-semibold text-erreur" role="alert">{{ loadError() }}</p>
      } @else {
        <h2 id="famille-titre" class="mb-4 text-xl font-bold">{{ savedName() }}</h2>

        <form class="panel" (submit)="saveFamily($event)" novalidate>
          <label class="field-label" for="nom">Nom du produit</label>
          <input id="nom" class="field" type="text" maxlength="200" autocomplete="off" [value]="name()" (input)="name.set($any($event.target).value)" [attr.aria-invalid]="nameError() ? 'true' : null" aria-describedby="nom-erreur" />
          <p id="nom-erreur" class="field-error">{{ nameError() }}</p>
          <p class="field-help">Ce nom est celui de tous les formats : « {{ savedName() }} · {{ exampleLabel() }} ».</p>

          <label class="field-label mt-4" for="categorie">Catégorie (facultatif)</label>
          <input id="categorie" class="field" type="text" maxlength="100" autocomplete="off" [value]="category()" (input)="category.set($any($event.target).value)" />

          @if (familyFailure()) {
            <p class="field-error mt-4" role="alert">{{ familyFailure() }}</p>
          }
          <button type="submit" class="btn-primary mt-4 w-full" [disabled]="saving()">{{ saving() ? 'Enregistrement…' : 'Enregistrer la famille' }}</button>
        </form>

        <section class="panel mt-4" aria-labelledby="photo-titre">
          <h3 id="photo-titre" class="mb-3 text-lg font-bold">Photo (facultatif)</h3>
          <app-photo-field
            [name]="savedName()" [photoId]="familyPhotoId()" [busy]="photoBusy()" [error]="photoError()"
            (picked)="pickPhoto($event)" (removed)="removePhoto()"
          />
        </section>

        <section class="mt-4" aria-labelledby="formats-titre">
          <h3 id="formats-titre" class="mb-2 text-lg font-bold">Formats de « {{ savedName() }} »</h3>
          <ul class="flex flex-col gap-2">
            @for (item of formats(); track item.id) {
              <li class="panel flex flex-wrap items-center gap-3">
                <div class="min-w-0 flex-1">
                  <p class="font-semibold">
                    {{ item.formatLabel ?? 'Format unique' }}
                    @if (!item.isActive) {
                      <span class="text-sm font-normal text-ardoise"> · Désactivé</span>
                    }
                  </p>
                  <p class="amount text-sm text-ardoise">
                    {{ item.barcode ? 'Code-barres ' + item.barcode + ' · ' : 'Sans code-barres · ' }}vente {{ format(item.salePriceFcfa) }}{{ perUnit(item.unit) }}
                    @if (item.purchasePriceFcfa !== null) {
                      · achat {{ format(item.purchasePriceFcfa) }}{{ perUnit(item.unit) }}
                    }
                    @if (item.tracksStock) {
                      · stock {{ quantity(item, item.quantityOnHand) }} · seuil {{ quantity(item, item.alertThreshold) }}
                    }
                  </p>
                </div>
                <a class="btn" [routerLink]="['/gestion/produits', item.id]" [attr.aria-label]="'Modifier le format ' + (item.formatLabel ?? item.name)">Modifier</a>
                @if (item.tracksStock) {
                  <a class="btn" [routerLink]="['/gestion/produits', item.id, 'stock']" [attr.aria-label]="'Stock du format ' + (item.formatLabel ?? item.name)">Stock</a>
                }
              </li>
            }
          </ul>

          @if (adding()) {
            <form class="panel mt-3" (submit)="addFormat($event)" novalidate aria-label="Nouveau format">
              @if (needsFirstLabel()) {
                <label class="field-label" for="premier-format">Format du produit actuel</label>
                <input id="premier-format" class="field" type="text" maxlength="40" autocomplete="off" placeholder="1 L, 50 kg…" [value]="firstLabel()" (input)="firstLabel.set($any($event.target).value)" [attr.aria-invalid]="firstLabelError() ? 'true' : null" aria-describedby="premier-format-erreur" />
                <p id="premier-format-erreur" class="field-error">{{ firstLabelError() }}</p>
                <p class="field-help">Dès qu’il y a deux formats, chacun doit avoir le sien.</p>
              }
              <div class="mt-3">
                <app-format-fields [draft]="draft()" [errors]="draftErrors()" idBase="nf" legend="Nouveau format" [requireLabel]="true" (patch)="draft.set({ ...draft(), ...$event })" />
              </div>
              @if (addFailure()) {
                <p class="field-error mt-3" role="alert">{{ addFailure() }}</p>
              }
              <div class="mt-3 grid grid-cols-2 gap-3">
                <button type="button" class="btn" (click)="adding.set(false)">Annuler</button>
                <button type="submit" class="btn-primary" [disabled]="saving()">Ajouter ce format</button>
              </div>
            </form>
          } @else {
            <button type="button" class="btn-primary mt-3" (click)="startAdding()">Ajouter un format</button>
          }
        </section>
      }
    </section>
  `,
})
export class FamilyPage implements OnInit {
  private readonly api = inject(ProductsApi);
  private readonly route = inject(ActivatedRoute);
  private readonly flash = inject(FlashService);
  private readonly images = inject(ImageService);

  protected readonly familyId = signal('');
  protected readonly loading = signal(true);
  protected readonly loadError = signal('');
  protected readonly formats = signal<ManagedProduct[]>([]);
  protected readonly name = signal('');
  protected readonly category = signal('');
  protected readonly savedName = signal('');
  protected readonly nameError = signal('');
  protected readonly familyFailure = signal('');
  protected readonly saving = signal(false);
  protected readonly photoBusy = signal(false);
  protected readonly photoError = signal('');

  protected readonly adding = signal(false);
  protected readonly draft = signal<FormatDraft>(newDraft());
  protected readonly draftErrors = signal<FormatErrors>({});
  protected readonly firstLabel = signal('');
  protected readonly firstLabelError = signal('');
  protected readonly addFailure = signal('');

  protected readonly familyPhotoId = computed(() => this.formats()[0]?.familyPhotoId ?? null);
  protected readonly exampleLabel = computed(() => this.formats().find((item) => item.formatLabel)?.formatLabel ?? '1 L');
  /** Une famille d'un seul format sans libellé : ajouter un second format oblige à nommer le premier. */
  protected readonly needsFirstLabel = computed(() => {
    const active = this.formats().filter((item) => item.isActive);
    return active.length === 1 && !active[0].formatLabel;
  });
  protected format = formatFcfa;
  protected perUnit = perUnit;

  protected quantity(item: ManagedProduct, value: number): string {
    return formatQuantity(value, item.unit);
  }

  async ngOnInit(): Promise<void> {
    this.familyId.set(this.route.snapshot.paramMap.get('familyId') ?? '');
    await this.load();
  }

  private async load(): Promise<void> {
    try {
      const all = await this.api.list({ includeInactive: true });
      const members = all
        .filter((item) => (item.familyId ?? item.id) === this.familyId())
        .sort((a, b) => a.salePriceFcfa - b.salePriceFcfa);
      if (members.length === 0) {
        this.loadError.set('Produit introuvable.');
        return;
      }
      this.formats.set(members);
      this.savedName.set(members[0].familyName ?? members[0].name);
      this.name.set(this.savedName());
      this.category.set(members[0].category ?? '');
    } catch (error) {
      this.loadError.set(describeError(error));
    } finally {
      this.loading.set(false);
    }
  }

  protected async saveFamily(event: Event): Promise<void> {
    event.preventDefault();
    if (this.saving()) {
      return;
    }
    this.familyFailure.set('');
    const name = this.name().trim();
    this.nameError.set(name === '' ? 'Le nom du produit est obligatoire.' : '');
    if (name === '') {
      return;
    }
    this.saving.set(true);
    try {
      await this.api.updateFamily(this.familyId(), { name, category: this.category().trim() || null });
      this.flash.show('Famille modifiée.');
      await this.load();
    } catch (error) {
      this.familyFailure.set(describeError(error));
    } finally {
      this.saving.set(false);
    }
  }

  protected async pickPhoto(file: File): Promise<void> {
    this.photoError.set('');
    this.photoBusy.set(true);
    try {
      await this.api.setPhoto('family', this.familyId(), await this.images.shrink(file));
      await this.load();
    } catch (error) {
      this.photoError.set(error instanceof Error ? error.message : describeError(error));
    } finally {
      this.photoBusy.set(false);
    }
  }

  protected async removePhoto(): Promise<void> {
    this.photoError.set('');
    this.photoBusy.set(true);
    try {
      await this.api.removePhoto('family', this.familyId());
      await this.load();
    } catch (error) {
      this.photoError.set(describeError(error));
    } finally {
      this.photoBusy.set(false);
    }
  }

  protected startAdding(): void {
    // Identifiant fixé à l'ouverture du formulaire : un nouvel essai après une coupure ne crée pas deux formats.
    this.draft.set(newDraft());
    this.draftErrors.set({});
    this.firstLabel.set('');
    this.firstLabelError.set('');
    this.addFailure.set('');
    this.adding.set(true);
  }

  protected async addFormat(event: Event): Promise<void> {
    event.preventDefault();
    if (this.saving()) {
      return;
    }
    this.addFailure.set('');
    const checked = validateFormats([this.draft()], true);
    this.draftErrors.set(checked.errors[0] ?? {});
    const first = this.firstLabel().trim();
    this.firstLabelError.set(this.needsFirstLabel() && first === '' ? 'Donnez un format au produit actuel (1 L, 50 kg…).' : '');
    if (!checked.inputs || this.firstLabelError()) {
      return;
    }
    this.saving.set(true);
    try {
      await this.api.addFormat(this.familyId(), checked.inputs[0], this.needsFirstLabel() ? first : null);
      this.flash.show('Format ajouté.');
      this.adding.set(false);
      await this.load();
    } catch (error) {
      this.addFailure.set(
        error instanceof ApiError && error.code === 'barcode_taken' ? 'Ce code-barres est déjà utilisé par un autre produit.' : describeError(error),
      );
    } finally {
      this.saving.set(false);
    }
  }
}
