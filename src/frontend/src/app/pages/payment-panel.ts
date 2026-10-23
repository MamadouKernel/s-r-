import { Component, computed, inject, input, OnInit, output, signal } from '@angular/core';
import { formatFcfa, parseFcfa } from '../core/money';
import { SaleOperation } from '../data/journal';
import { cashChange, validatePayments } from '../domain/payment';
import { MOBILE_MONEY_METHODS, PaymentDraft, PaymentMethod } from '../domain/types';
import { CashService } from '../services/cash.service';
import { SyncService } from '../services/sync.service';

interface Row {
  id: string;
  method: PaymentMethod;
  amount: string;
  tendered: string;
  reference: string;
}

/** S5 : espèces, Mobile Money et paiement mixte, avec récapitulatif avant validation (PWA-05). */
@Component({
  selector: 'app-payment-panel',
  template: `
    @if (confirmed(); as sale) {
      <section class="panel" aria-labelledby="paiement-titre" role="status">
        <h2 id="paiement-titre" class="text-xl font-bold text-succes">Vente enregistrée</h2>
        <p class="mt-1 text-base">
          @if (sync.counts().pending > 0) {
            Enregistrée sur l’appareil, en attente de synchronisation.
          } @else {
            Enregistrée et envoyée au serveur.
          }
        </p>
        <p class="amount mt-4 text-2xl font-bold">{{ format(sale.netTotalFcfa) }}</p>
        @if (sale.changeFcfa > 0) {
          <p class="amount mt-2 text-2xl font-bold text-feuille">Monnaie à rendre : {{ format(sale.changeFcfa) }}</p>
        }
        <button type="button" class="btn-primary mt-6 w-full" (click)="finished.emit()">Nouvelle vente</button>
      </section>
    } @else {
      <section class="panel" aria-labelledby="paiement-titre">
        <h2 id="paiement-titre" class="text-xl font-bold">Paiement</h2>
        <p class="amount mt-1 text-2xl font-bold">À payer : {{ format(total()) }}</p>

        @for (row of rows(); track row.id; let index = $index) {
          <fieldset class="mt-4 border-t border-sauge pt-4">
            <legend class="field-label">Paiement {{ index + 1 }}</legend>

            <label class="field-label" [for]="'mode-' + row.id">Mode</label>
            <!-- [selected] sur chaque option : [value] sur le select est appliqué avant la création des options. -->
            <select class="field" [id]="'mode-' + row.id" (change)="setMethod(index, $any($event.target).value)">
              <option value="Cash" [selected]="row.method === 'Cash'">Espèces</option>
              @for (option of mobileMoney; track option.method) {
                <option [value]="option.method" [selected]="row.method === option.method">{{ option.label }}</option>
              }
            </select>

            <label class="field-label mt-3" [for]="'montant-' + row.id">Montant (FCFA)</label>
            <input
              class="field amount text-right"
              type="text"
              inputmode="numeric"
              autocomplete="off"
              [id]="'montant-' + row.id"
              [value]="row.amount"
              (input)="patch(index, { amount: $any($event.target).value })"
            />

            @if (row.method === 'Cash') {
              <label class="field-label mt-3" [for]="'recu-' + row.id">Montant reçu (FCFA)</label>
              <input
                class="field amount text-right"
                type="text"
                inputmode="numeric"
                autocomplete="off"
                [id]="'recu-' + row.id"
                [value]="row.tendered"
                (input)="patch(index, { tendered: $any($event.target).value })"
              />
              @if (changeOf(row); as change) {
                <p class="amount mt-2 text-lg font-bold text-feuille">Monnaie à rendre : {{ format(change) }}</p>
              }
            } @else {
              <label class="field-label mt-3" [for]="'ref-' + row.id">Référence de la transaction</label>
              <input
                class="field font-mono"
                type="text"
                autocomplete="off"
                autocapitalize="characters"
                spellcheck="false"
                [id]="'ref-' + row.id"
                [value]="row.reference"
                (input)="patch(index, { reference: $any($event.target).value })"
              />
              <p class="field-help">Recopiez la référence du SMS de confirmation reçu par le client.</p>
            }

            @if (rows().length > 1) {
              <button type="button" class="btn mt-3" (click)="removeRow(index)">Retirer ce paiement</button>
            }
          </fieldset>
        }

        <div class="mt-4 flex flex-wrap items-center gap-3">
          <button type="button" class="btn" (click)="addRow()" [disabled]="remaining() <= 0">Ajouter un paiement</button>
          <p class="amount text-base" [class.text-erreur]="remaining() !== 0" [class.text-succes]="remaining() === 0">
            @if (remaining() > 0) {
              Reste à payer : {{ format(remaining()) }}
            } @else if (remaining() < 0) {
              Trop perçu : {{ format(-remaining()) }}
            } @else {
              Montant complet
            }
          </p>
        </div>

        @if (errors().length > 0 && touched()) {
          <ul class="mt-4 list-disc pl-5 text-base font-semibold text-erreur" role="alert">
            @for (message of errors(); track message) {
              <li>{{ message }}</li>
            }
          </ul>
        }
        @if (failure()) {
          <p class="mt-4 text-base font-semibold text-erreur" role="alert">{{ failure() }}</p>
        }

        <div class="mt-6 grid grid-cols-2 gap-3">
          <button type="button" class="btn" (click)="cancelled.emit()" [disabled]="cash.submitting()">Retour au panier</button>
          <button type="button" class="btn-primary" (click)="validate()" [disabled]="cash.submitting()">
            {{ cash.submitting() ? 'Enregistrement…' : 'Valider la vente' }}
          </button>
        </div>
      </section>
    }
  `,
})
export class PaymentPanel implements OnInit {
  protected readonly cash = inject(CashService);
  protected readonly sync = inject(SyncService);

  readonly total = input.required<number>();
  readonly finished = output<void>();
  readonly cancelled = output<void>();

  protected readonly mobileMoney = MOBILE_MONEY_METHODS;
  protected readonly rows = signal<Row[]>([]);
  protected readonly confirmed = signal<SaleOperation | null>(null);
  protected readonly failure = signal('');
  protected readonly touched = signal(false);
  private readonly used = signal<ReadonlySet<string>>(new Set());

  protected readonly drafts = computed<PaymentDraft[]>(() => this.rows().map((row) => this.toDraft(row)));
  protected readonly remaining = computed(() => this.total() - this.drafts().reduce((sum, d) => sum + d.amountFcfa, 0));
  protected readonly errors = computed(() => validatePayments(this.total(), this.drafts(), this.used()));

  async ngOnInit(): Promise<void> {
    this.rows.set([this.newRow('Cash', this.total())]);
    this.used.set(await this.cash.usedReferences());
  }

  protected format = formatFcfa;

  protected changeOf(row: Row): number {
    const amount = parseFcfa(row.amount);
    const tendered = parseFcfa(row.tendered);
    return amount !== null && tendered !== null && tendered > amount ? cashChange(amount, tendered) : 0;
  }

  protected setMethod(index: number, method: PaymentMethod): void {
    this.patch(index, { method });
  }

  protected patch(index: number, change: Partial<Row>): void {
    this.rows.update((rows) =>
      rows.map((row, position) => {
        if (position !== index) {
          return row;
        }
        const next = { ...row, ...change };
        // Tant que le client n'a pas précisé un autre montant reçu, on le suit : le cas courant est « le compte est bon ».
        if (change.amount !== undefined && row.tendered === row.amount) {
          next.tendered = change.amount;
        }
        return next;
      }),
    );
  }

  protected addRow(): void {
    this.rows.update((rows) => [...rows, this.newRow('OrangeMoney', Math.max(this.remaining(), 0))]);
  }

  protected removeRow(index: number): void {
    this.rows.update((rows) => rows.filter((_, position) => position !== index));
  }

  protected async validate(): Promise<void> {
    this.touched.set(true);
    this.failure.set('');
    if (this.errors().length > 0) {
      return;
    }
    try {
      this.confirmed.set(await this.cash.confirmSale(this.drafts()));
    } catch (error) {
      this.failure.set(error instanceof Error ? error.message : 'La vente n’a pas pu être enregistrée. Réessayez.');
    }
  }

  private newRow(method: PaymentMethod, amount: number): Row {
    return { id: crypto.randomUUID(), method, amount: String(amount), tendered: String(amount), reference: '' };
  }

  private toDraft(row: Row): PaymentDraft {
    const amount = parseFcfa(row.amount) ?? 0;
    return row.method === 'Cash'
      ? { id: row.id, method: 'Cash', amountFcfa: amount, tenderedFcfa: parseFcfa(row.tendered) ?? 0 }
      : { id: row.id, method: row.method, amountFcfa: amount, reference: row.reference };
  }

}
