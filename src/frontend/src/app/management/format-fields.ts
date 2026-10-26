import { Component, computed, DestroyRef, inject, input, output } from '@angular/core';
import { perUnit, unitSymbol } from '../core/measure';
import { ScanService } from '../services/scan.service';
import { FormatDraft, FormatErrors } from './format-draft';

/** Les champs d'un format (maquette « créer le riz ») : format, code-barres, prix d'achat et de vente, alerte de stock, suivi du stock. */
@Component({
  selector: 'app-format-fields',
  template: `
    <fieldset class="rounded border border-sauge bg-ivoire p-3">
      <legend class="px-1 text-base font-semibold">{{ legend() }}</legend>
      <div class="grid gap-3 sm:grid-cols-2">
        <div class="sm:col-span-2">
          <label class="field-label" [attr.for]="id('unite')">Vendu</label>
          <select
            [id]="id('unite')" class="field" [value]="draft().unit" [attr.aria-describedby]="id('unite-aide')"
            (change)="patch.emit({ unit: $any($event.target).value })"
          >
            <option value="Piece" [selected]="draft().unit === 'Piece'">À la pièce</option>
            <option value="Kilogram" [selected]="draft().unit === 'Kilogram'">Au poids (kg)</option>
            <option value="Litre" [selected]="draft().unit === 'Litre'">À la mesure (litre)</option>
          </select>
          <p [id]="id('unite-aide')" class="field-help">
            @if (draft().unit === 'Piece') {
              Chaque article se compte un par un. Ce choix ne pourra plus changer une fois le produit en vente.
            } @else {
              Le prix ci-dessous est celui d’un {{ draft().unit === 'Kilogram' ? 'kilo' : 'litre' }} ; la caisse calcule le prix selon la quantité servie. Ce choix ne pourra plus changer une fois le produit en vente.
            }
          </p>
        </div>
        <div>
          <label class="field-label" [attr.for]="id('format')">Format{{ requireLabel() ? '' : ' (facultatif)' }}</label>
          <input
            [id]="id('format')" class="field" type="text" maxlength="40" autocomplete="off" placeholder="1 L, 50 kg, Pack de 6…"
            [value]="draft().label" (input)="patch.emit({ label: $any($event.target).value })"
            [attr.aria-invalid]="errors().label ? 'true' : null" [attr.aria-describedby]="id('format-erreur')"
          />
          <p [id]="id('format-erreur')" class="field-error">{{ errors().label }}</p>
        </div>
        <div>
          <label class="field-label" [attr.for]="id('code-barres')">Code-barres (facultatif)</label>
          <input
            [id]="id('code-barres')" class="field font-mono" type="text" maxlength="64" autocomplete="off" inputmode="numeric"
            [value]="draft().barcode" (input)="patch.emit({ barcode: $any($event.target).value })"
            [attr.aria-invalid]="errors().barcode ? 'true' : null" [attr.aria-describedby]="id('code-erreur')"
          />
          <p [id]="id('code-erreur')" class="field-error">{{ errors().barcode }}</p>
        </div>
        <div>
          <label class="field-label" [attr.for]="id('prix-achat')">Prix d’achat (FCFA{{ priceUnit() }})</label>
          <input
            [id]="id('prix-achat')" class="field amount text-right" type="text" inputmode="numeric" autocomplete="off"
            [value]="draft().purchasePrice" (input)="patch.emit({ purchasePrice: $any($event.target).value })"
            [attr.aria-invalid]="errors().purchasePrice ? 'true' : null" [attr.aria-describedby]="id('prix-achat-erreur')"
          />
          <p [id]="id('prix-achat-erreur')" class="field-error">{{ errors().purchasePrice }}</p>
        </div>
        <div>
          <label class="field-label" [attr.for]="id('prix-vente')">Prix de vente (FCFA{{ priceUnit() }})</label>
          <input
            [id]="id('prix-vente')" class="field amount text-right" type="text" inputmode="numeric" autocomplete="off"
            [value]="draft().salePrice" (input)="patch.emit({ salePrice: $any($event.target).value })"
            [attr.aria-invalid]="errors().salePrice ? 'true' : null" [attr.aria-describedby]="id('prix-vente-erreur')"
          />
          <p [id]="id('prix-vente-erreur')" class="field-error">{{ errors().salePrice }}</p>
        </div>
        <div>
          <label class="field-label" [attr.for]="id('seuil')">Alerte quand il reste{{ thresholdUnit() }}</label>
          <input
            [id]="id('seuil')" class="field amount text-right" type="text" [attr.inputmode]="draft().unit === 'Piece' ? 'numeric' : 'decimal'" autocomplete="off"
            [value]="draft().threshold" (input)="patch.emit({ threshold: $any($event.target).value })"
            [attr.aria-invalid]="errors().threshold ? 'true' : null" [attr.aria-describedby]="id('seuil-erreur')"
          />
          <p [id]="id('seuil-erreur')" class="field-error">{{ errors().threshold }}</p>
        </div>
      </div>
      <label class="mt-3 flex min-h-12 items-center gap-2 text-base">
        <input
          [id]="id('stock')" type="checkbox" class="h-6 w-6" [checked]="draft().tracksStock"
          (change)="patch.emit({ tracksStock: $any($event.target).checked })"
        />
        Suivre le stock de ce format
      </label>
      @if (removable()) {
        <button type="button" class="btn mt-2" [attr.aria-label]="'Retirer ' + legend()" (click)="remove.emit()">Retirer ce format</button>
      }
    </fieldset>
  `,
})
export class FormatFields {
  readonly draft = input.required<FormatDraft>();
  readonly errors = input<FormatErrors>({});
  /** Début des identifiants des champs : « f0 » donne « f0-format », « f0-prix-vente »… */
  readonly idBase = input.required<string>();
  readonly legend = input('Format');
  readonly requireLabel = input(true);
  readonly removable = input(false);

  /** « par kg » à côté du prix : sans lui, 600 FCFA pourrait être le prix du sac. */
  protected readonly priceUnit = computed(() => perUnit(this.draft().unit));
  protected readonly thresholdUnit = computed(() => (this.draft().unit === 'Piece' ? '' : ` (en ${unitSymbol(this.draft().unit)})`));

  readonly patch = output<Partial<FormatDraft>>();
  readonly remove = output<void>();

  constructor() {
    // Lecteur de codes-barres (ADR 0015) : un scan dans la zone « Code-barres » de ce format la remplit, et Entrée n'enregistre pas la fiche.
    const stop = inject(ScanService).listen({
      accept: (target) => (target as HTMLElement | null)?.id === this.id('code-barres'),
      onScan: (scan) => this.patch.emit({ barcode: scan.code }),
    });
    inject(DestroyRef).onDestroy(stop);
  }

  protected id(name: string): string {
    return `${this.idBase()}-${name}`;
  }
}
