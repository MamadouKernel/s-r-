import { Component, DestroyRef, inject, OnInit, signal } from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { parseFcfa } from '../core/money';
import { ImageService } from '../core/image';
import { ScanService } from '../services/scan.service';
import { ApiError } from '../sync/sync-api';
import { describeError } from './errors';
import { FlashService } from './flash.service';
import { PhotoField } from './photo-field';
import { ManagedProduct, ProductsApi } from './products-api';

interface FieldErrors {
  name?: string;
  label?: string;
  barcode?: string;
  salePrice?: string;
  purchasePrice?: string;
  threshold?: string;
}

/**
 * GES-02 : modification d'un format (un produit vendable) et désactivation avec confirmation. La création passe par `FamilyFormPage`. Le nom et la
 * catégorie sont ceux de la famille : les changer renomme tous ses formats.
 */
@Component({
  selector: 'app-product-form-page',
  imports: [RouterLink, PhotoField],
  template: `
    <section class="mx-auto max-w-xl" aria-labelledby="fiche-titre">
      <a class="btn mb-4" routerLink="/gestion/produits">← Retour aux produits</a>
      <h2 id="fiche-titre" class="mb-4 text-xl font-bold">Modifier le produit</h2>

      @if (loading()) {
        <p class="panel text-ardoise" role="status">Chargement du produit…</p>
      } @else if (loadError()) {
        <div class="panel" role="alert">
          <p class="font-semibold text-erreur">{{ loadError() }}</p>
        </div>
      } @else {
        <form class="panel" (submit)="save($event)" novalidate>
          <label class="field-label" for="nom">Nom du produit</label>
          <input id="nom" class="field" type="text" maxlength="200" autocomplete="off" [value]="name()" (input)="name.set($any($event.target).value)" [attr.aria-invalid]="errors().name ? 'true' : null" aria-describedby="nom-erreur" />
          <p id="nom-erreur" class="field-error">{{ errors().name }}</p>
          @if (siblings() > 1) {
            <p class="field-help">
              Ce nom est celui de toute la famille ({{ siblings() }} formats) :
              <a class="font-semibold underline" [routerLink]="['/gestion/produits/famille', familyId()]">ouvrir la famille</a>.
            </p>
          }

          <label class="field-label mt-4" for="format">Format{{ siblings() > 1 ? '' : ' (facultatif)' }}</label>
          <input id="format" class="field" type="text" maxlength="40" autocomplete="off" placeholder="1 L, 50 kg…" [value]="label()" (input)="label.set($any($event.target).value)" [attr.aria-invalid]="errors().label ? 'true' : null" aria-describedby="format-erreur" />
          <p id="format-erreur" class="field-error">{{ errors().label }}</p>

          <label class="field-label mt-4" for="code-barres">Code-barres (facultatif)</label>
          <input id="code-barres" class="field font-mono" type="text" maxlength="64" autocomplete="off" inputmode="numeric" [value]="barcode()" (input)="barcode.set($any($event.target).value)" [attr.aria-invalid]="errors().barcode ? 'true' : null" aria-describedby="code-aide code-erreur" />
          <p id="code-aide" class="field-help">Un même code-barres ne peut servir qu’à un seul produit.</p>
          <p id="code-erreur" class="field-error">{{ errors().barcode }}</p>

          <label class="field-label mt-4" for="categorie">Catégorie (facultatif)</label>
          <input id="categorie" class="field" type="text" maxlength="100" autocomplete="off" [value]="category()" (input)="category.set($any($event.target).value)" />

          <label class="field-label mt-4" for="prix-vente">Prix de vente (FCFA)</label>
          <input id="prix-vente" class="field amount text-right" type="text" inputmode="numeric" autocomplete="off" [value]="salePrice()" (input)="salePrice.set($any($event.target).value)" [attr.aria-invalid]="errors().salePrice ? 'true' : null" aria-describedby="prix-vente-erreur" />
          <p id="prix-vente-erreur" class="field-error">{{ errors().salePrice }}</p>

          <label class="field-label mt-4" for="prix-achat">Prix d’achat moyen (FCFA)</label>
          <input id="prix-achat" class="field amount text-right" type="text" inputmode="numeric" autocomplete="off" [value]="purchasePrice()" (input)="purchasePrice.set($any($event.target).value)" [attr.aria-invalid]="errors().purchasePrice ? 'true' : null" aria-describedby="prix-achat-aide prix-achat-erreur" />
          <p id="prix-achat-aide" class="field-help">Recalculé à chaque réception où vous indiquez le prix de la livraison. Le modifier ici le remplace.</p>
          <p id="prix-achat-erreur" class="field-error">{{ errors().purchasePrice }}</p>

          <label class="field-label mt-4" for="seuil">Seuil d’alerte de stock</label>
          <input id="seuil" class="field amount text-right" type="text" inputmode="numeric" autocomplete="off" [value]="threshold()" (input)="threshold.set($any($event.target).value)" [attr.aria-invalid]="errors().threshold ? 'true' : null" aria-describedby="seuil-aide seuil-erreur" />
          <p id="seuil-aide" class="field-help">Une alerte apparaît quand le stock atteint ce nombre. 0 : pas d’alerte.</p>
          <p id="seuil-erreur" class="field-error">{{ errors().threshold }}</p>

          <label class="mt-4 flex items-center gap-2 text-base">
            <input type="checkbox" class="h-6 w-6" [checked]="tracksStock()" disabled />
            Suivre le stock de ce produit
          </label>
          <p class="field-help">Ce choix se fait à la création et ne peut plus changer.</p>

          @if (failure()) {
            <p class="field-error mt-4" role="alert">{{ failure() }}</p>
          }
          <button type="submit" class="btn-primary mt-6 w-full" [disabled]="saving()">{{ saving() ? 'Enregistrement…' : 'Enregistrer' }}</button>
        </form>

        <section class="panel mt-4" aria-labelledby="photo-format-titre">
          <h3 id="photo-format-titre" class="mb-3 text-lg font-bold">Photo de ce format (facultatif)</h3>
          <app-photo-field
            [name]="displayName()" [photoId]="effectivePhotoId()" [canRemove]="hasOwnPhoto()" [busy]="photoBusy()" [error]="photoError()"
            (picked)="pickPhoto($event)" (removed)="removePhoto()"
          />
          <p class="field-help">Sans photo propre, ce format montre celle de la famille.</p>
        </section>

        @if (active()) {
          <section class="panel mt-4" aria-labelledby="desactiver-titre">
            <h3 id="desactiver-titre" class="text-lg font-bold">Désactiver ce produit</h3>
            <p class="mt-1 text-base text-ardoise">
              Il n’apparaîtra plus à la vente, mais il reste dans l’historique des ventes et des mouvements de stock. Rien n’est supprimé.
            </p>
            @if (confirmingDeactivation()) {
              <p class="mt-3 font-semibold text-erreur" role="alert">Désactiver « {{ displayName() }} » ?</p>
              <div class="mt-2 grid grid-cols-2 gap-3">
                <button type="button" class="btn" (click)="confirmingDeactivation.set(false)">Non, le garder</button>
                <button type="button" class="btn-primary !border-erreur !bg-erreur" (click)="deactivate()" [disabled]="saving()">Oui, désactiver</button>
              </div>
            } @else {
              <button type="button" class="btn mt-3" (click)="confirmingDeactivation.set(true)">Désactiver ce produit</button>
            }
          </section>
        }
      }
    </section>
  `,
})
export class ProductFormPage implements OnInit {
  private readonly api = inject(ProductsApi);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly flash = inject(FlashService);
  private readonly images = inject(ImageService);

  protected readonly productId = signal<string>('');
  protected readonly familyId = signal('');
  protected readonly siblings = signal(1);
  protected readonly displayName = signal('');
  protected readonly effectivePhotoId = signal<string | null>(null);
  protected readonly hasOwnPhoto = signal(false);
  protected readonly photoBusy = signal(false);
  protected readonly photoError = signal('');
  protected readonly label = signal('');
  protected readonly loading = signal(false);
  protected readonly loadError = signal('');
  protected readonly saving = signal(false);
  protected readonly failure = signal('');
  protected readonly errors = signal<FieldErrors>({});
  protected readonly active = signal(true);
  protected readonly confirmingDeactivation = signal(false);

  protected readonly name = signal('');
  protected readonly barcode = signal('');
  protected readonly category = signal('');
  protected readonly salePrice = signal('');
  protected readonly purchasePrice = signal('0');
  protected readonly threshold = signal('0');
  protected readonly tracksStock = signal(true);

  constructor() {
    // Lecteur de codes-barres (ADR 0015) : un scan dans la zone « Code-barres » la remplit, et Entrée n'enregistre pas la fiche.
    const stop = inject(ScanService).listen({
      accept: (target) => (target as HTMLElement | null)?.id === 'code-barres',
      onScan: (scan) => this.barcode.set(scan.code),
    });
    inject(DestroyRef).onDestroy(stop);
  }

  async ngOnInit(): Promise<void> {
    const id = this.route.snapshot.paramMap.get('id') ?? '';
    this.productId.set(id);
    await this.loadProduct(id);
  }

  private async loadProduct(id: string): Promise<void> {
    this.loading.set(true);
    try {
      const all = await this.api.list({ includeInactive: true });
      const product = all.find((candidate) => candidate.id === id);
      if (!product) {
        this.loadError.set('Produit introuvable.');
        return;
      }
      this.familyId.set(product.familyId ?? product.id);
      this.siblings.set(all.filter((candidate) => (candidate.familyId ?? candidate.id) === this.familyId()).length);
      this.fill(product);
    } catch (error) {
      this.loadError.set(describeError(error));
    } finally {
      this.loading.set(false);
    }
  }

  private fill(product: ManagedProduct): void {
    this.displayName.set(product.name);
    this.name.set(product.familyName ?? product.name);
    this.label.set(product.formatLabel ?? '');
    this.effectivePhotoId.set(product.photoId ?? null);
    this.hasOwnPhoto.set(!!product.photoId && product.photoId !== (product.familyPhotoId ?? null));
    this.barcode.set(product.barcode ?? '');
    this.category.set(product.category ?? '');
    this.salePrice.set(String(product.salePriceFcfa));
    this.purchasePrice.set(String(product.purchasePriceFcfa ?? 0));
    this.threshold.set(String(product.alertThreshold));
    this.tracksStock.set(product.tracksStock);
    this.active.set(product.isActive);
  }

  protected async save(event: Event): Promise<void> {
    event.preventDefault();
    if (this.saving()) {
      return;
    }
    this.failure.set('');
    const checked = this.validate();
    this.errors.set(checked.errors);
    if (!checked.input) {
      return;
    }

    this.saving.set(true);
    try {
      await this.api.update(this.productId(), checked.input);
      this.flash.show('Produit modifié.');
      await this.router.navigateByUrl('/gestion/produits');
    } catch (error) {
      if (error instanceof ApiError && error.code === 'barcode_taken') {
        this.errors.set({ barcode: 'Ce code-barres est déjà utilisé par un autre produit.' });
      } else if (error instanceof ApiError && error.code === 'rule_violation') {
        this.errors.set({ label: error.message });
      } else {
        this.failure.set(describeError(error));
      }
    } finally {
      this.saving.set(false);
    }
  }

  protected async pickPhoto(file: File): Promise<void> {
    this.photoError.set('');
    this.photoBusy.set(true);
    try {
      const photoId = await this.api.setPhoto('format', this.productId(), await this.images.shrink(file));
      this.effectivePhotoId.set(photoId);
      this.hasOwnPhoto.set(true);
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
      await this.api.removePhoto('format', this.productId());
      await this.loadProduct(this.productId());
    } catch (error) {
      this.photoError.set(describeError(error));
    } finally {
      this.photoBusy.set(false);
    }
  }

  protected async deactivate(): Promise<void> {
    this.saving.set(true);
    this.failure.set('');
    try {
      await this.api.deactivate(this.productId());
      this.flash.show('Produit désactivé.');
      await this.router.navigateByUrl('/gestion/produits');
    } catch (error) {
      this.failure.set(describeError(error));
      this.confirmingDeactivation.set(false);
    } finally {
      this.saving.set(false);
    }
  }

  private validate(): {
    errors: FieldErrors;
    input: {
      name: string;
      barcode: string | null;
      category: string | null;
      purchasePriceFcfa: number;
      salePriceFcfa: number;
      alertThreshold: number;
      formatLabel: string | null;
    } | null;
  } {
    const errors: FieldErrors = {};
    const name = this.name().trim();
    const salePrice = parseFcfa(this.salePrice());
    const purchasePrice = parseFcfa(this.purchasePrice());
    const threshold = parseFcfa(this.threshold());

    const label = this.label().split(/\s+/).filter(Boolean).join(' ');
    if (name === '') {
      errors.name = 'Le nom du produit est obligatoire.';
    }
    if (label === '' && this.siblings() > 1) {
      errors.label = 'Donnez un format à ce produit (1 L, 50 kg…).';
    }
    if (salePrice === null) {
      errors.salePrice = 'Saisissez le prix de vente en chiffres, sans virgule.';
    }
    if (purchasePrice === null) {
      errors.purchasePrice = 'Saisissez le prix d’achat en chiffres, sans virgule (0 s’il est inconnu).';
    }
    if (threshold === null) {
      errors.threshold = 'Saisissez le seuil en chiffres entiers (0 pour aucune alerte).';
    }
    if (Object.keys(errors).length > 0 || salePrice === null || purchasePrice === null || threshold === null) {
      return { errors, input: null };
    }
    return {
      errors,
      input: {
        name,
        barcode: this.barcode().trim() || null,
        category: this.category().trim() || null,
        purchasePriceFcfa: purchasePrice,
        salePriceFcfa: salePrice,
        alertThreshold: threshold,
        formatLabel: label === '' ? null : label,
      },
    };
  }
}
