import { Component, input, output } from '@angular/core';
import { FormatDraft, FormatErrors } from './format-draft';

/** Les champs d'un format (maquette « créer le riz ») : format, code-barres, prix d'achat et de vente, alerte de stock, suivi du stock. */
@Component({
  selector: 'app-format-fields',
  template: `
    <fieldset class="rounded border border-sauge bg-ivoire p-3">
      <legend class="px-1 text-base font-semibold">{{ legend() }}</legend>
      <div class="grid gap-3 sm:grid-cols-2">
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
          <label class="field-label" [attr.for]="id('prix-achat')">Prix d’achat (FCFA)</label>
          <input
            [id]="id('prix-achat')" class="field amount text-right" type="text" inputmode="numeric" autocomplete="off"
            [value]="draft().purchasePrice" (input)="patch.emit({ purchasePrice: $any($event.target).value })"
            [attr.aria-invalid]="errors().purchasePrice ? 'true' : null" [attr.aria-describedby]="id('prix-achat-erreur')"
          />
          <p [id]="id('prix-achat-erreur')" class="field-error">{{ errors().purchasePrice }}</p>
        </div>
        <div>
          <label class="field-label" [attr.for]="id('prix-vente')">Prix de vente (FCFA)</label>
          <input
            [id]="id('prix-vente')" class="field amount text-right" type="text" inputmode="numeric" autocomplete="off"
            [value]="draft().salePrice" (input)="patch.emit({ salePrice: $any($event.target).value })"
            [attr.aria-invalid]="errors().salePrice ? 'true' : null" [attr.aria-describedby]="id('prix-vente-erreur')"
          />
          <p [id]="id('prix-vente-erreur')" class="field-error">{{ errors().salePrice }}</p>
        </div>
        <div>
          <label class="field-label" [attr.for]="id('seuil')">Alerte quand il reste</label>
          <input
            [id]="id('seuil')" class="field amount text-right" type="text" inputmode="numeric" autocomplete="off"
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

  readonly patch = output<Partial<FormatDraft>>();
  readonly remove = output<void>();

  protected id(name: string): string {
    return `${this.idBase()}-${name}`;
  }
}
