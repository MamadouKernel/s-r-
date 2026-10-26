import { Component, computed, input, linkedSignal, output, signal } from '@angular/core';
import { amountToQuantity, formatQuantity, lineTotal, parseQuantity, unitSymbol } from '../core/measure';
import { formatFcfa, parseFcfa } from '../core/money';
import { CatalogueProduct } from '../domain/types';

type Mode = 'quantity' | 'amount';

/**
 * Saisie du poids ou de la mesure d'un produit vendu au kilo ou au litre (docs/specs/vente-au-poids VPO-06) : on tape le poids, ou le montant
 * (« pour 1 000 francs de riz »). Le résultat (« 1,250 kg · 1 000 FCFA ») est montré avant l'ajout au panier.
 */
@Component({
  selector: 'app-weigh-panel',
  template: `
    <section class="panel mb-4 border-feuille" aria-labelledby="peser-titre">
      <h3 id="peser-titre" class="text-lg font-bold">{{ current() !== null ? 'Modifier le poids' : 'Peser' }} : {{ product().name }}</h3>
      <p class="amount text-feuille font-bold">{{ priceText() }}</p>

      <div class="mt-3 flex gap-2" role="group" aria-label="Saisir par">
        <button type="button" class="btn" [attr.aria-pressed]="mode() === 'quantity'" [class.border-feuille]="mode() === 'quantity'" (click)="setMode('quantity')">{{ quantityLabel() }}</button>
        <button type="button" class="btn" [attr.aria-pressed]="mode() === 'amount'" [class.border-feuille]="mode() === 'amount'" (click)="setMode('amount')">Montant (FCFA)</button>
      </div>

      <label class="field-label mt-3" for="peser-valeur">{{ mode() === 'amount' ? 'Montant en FCFA' : fieldLabel() }}</label>
      <input id="peser-valeur" class="field amount text-right" type="text" inputmode="decimal" autocomplete="off" [value]="text()" (input)="text.set($any($event.target).value)" />

      @if (text().trim() === '') {
        <p class="field-help">{{ mode() === 'amount' ? 'Tapez le montant, par exemple 1000.' : 'Tapez ' + (unit() === 'Litre' ? 'la quantité' : 'le poids') + ', par exemple 1,5.' }}</p>
      } @else if (outcome().error) {
        <p class="field-error" role="alert">{{ outcome().error }}</p>
      } @else if (outcome().words) {
        <p class="mt-2 text-xl font-bold" role="status">{{ outcome().words }}</p>
      }

      <div class="mt-4 flex flex-wrap gap-2">
        <button type="button" class="btn-primary" [disabled]="outcome().quantity === null" (click)="add()">{{ current() !== null ? 'Enregistrer' : 'Ajouter au panier' }}</button>
        <button type="button" class="btn" (click)="cancelled.emit()">Annuler</button>
      </div>
    </section>
  `,
})
export class WeighPanel {
  readonly product = input.required<CatalogueProduct>();
  /** Ce qui reste à vendre (après ce qui est déjà au panier) ; `null` quand le stock n'est pas suivi. */
  readonly available = input<number | null>(null);
  /** La quantité actuelle d'une ligne du panier qu'on corrige. */
  readonly current = input<number | null>(null);

  readonly confirmed = output<number>();
  readonly cancelled = output<void>();

  protected readonly mode = signal<Mode>('quantity');

  /** Ce que tape le caissier ; repart de la quantité actuelle d'une ligne qu'on corrige, et se vide quand on change de mode. */
  protected readonly text = linkedSignal({
    source: () => ({ mode: this.mode(), current: this.current() }),
    computation: ({ mode }) => (mode === 'quantity' ? this.initial() : ''),
  });

  protected readonly unit = computed(() => this.product().unit ?? 'Kilogram');

  protected readonly priceText = computed(() => `${formatFcfa(this.product().salePriceFcfa)} / ${unitSymbol(this.unit())}`);
  protected readonly quantityLabel = computed(() => (this.unit() === 'Litre' ? 'Litres (L)' : 'Poids (kg)'));
  protected readonly fieldLabel = computed(() => (this.unit() === 'Litre' ? 'Quantité en litres' : 'Poids en kg'));

  protected readonly outcome = computed<{ quantity: number | null; error: string; words: string }>(() => {
    const product = this.product();
    const unit = this.unit();
    const text = this.text();
    let quantity: number;
    if (this.mode() === 'amount') {
      const amount = parseFcfa(text);
      const result = amountToQuantity(amount ?? 0, product.salePriceFcfa, unit);
      if (!result.ok) {
        return { quantity: null, error: amount === null ? 'Écrivez un montant en FCFA.' : result.message, words: '' };
      }
      quantity = result.quantity;
    } else {
      const result = parseQuantity(text, unit);
      if (!result.ok) {
        return { quantity: null, error: result.message, words: '' };
      }
      quantity = result.quantity;
    }

    const left = this.available();
    if (left !== null && quantity > left) {
      return { quantity: null, error: left <= 0 ? 'Il n’en reste plus.' : `Il n’en reste que ${formatQuantity(left, unit)}.`, words: '' };
    }
    return { quantity, error: '', words: `${formatQuantity(quantity, unit)} · ${formatFcfa(lineTotal(product.salePriceFcfa, quantity, unit))}` };
  });

  protected setMode(mode: Mode): void {
    this.mode.set(mode);
  }

  protected add(): void {
    const quantity = this.outcome().quantity;
    if (quantity !== null) {
      this.confirmed.emit(quantity);
    }
  }

  private initial(): string {
    const current = this.current();
    return current === null ? '' : formatQuantity(current, this.unit()).replace(/ | ?(kg|L)$/g, '').trim();
  }
}
