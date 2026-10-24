import { Component, computed, inject, input, OnInit, output, signal } from '@angular/core';
import { formatFcfa, parseFcfa } from '../core/money';
import { SaleOperation } from '../data/journal';
import { CustomerService } from '../customers/customer.service';
import { cashChange, creditProblem, validatePayments } from '../domain/payment';
import { CustomerSnapshot, MOBILE_MONEY_METHODS, PaymentDraft, PaymentMethod } from '../domain/types';
import { onboardingError } from '../onboarding/wording';
import { AuthService } from '../services/auth.service';
import { CashService } from '../services/cash.service';
import { NetworkService } from '../services/network.service';
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
        @if (soldOnCredit(); as credit) {
          <p class="amount mt-2 text-lg font-bold" role="status">
            À crédit pour {{ credit.name }} : {{ format(credit.amount) }}. Sa dette passe à {{ format(credit.newDebt) }}.
          </p>
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
              <option value="Credit" [selected]="row.method === 'Credit'">Crédit (le client paiera plus tard)</option>
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

            @if (row.method === 'Credit') {
              <p class="field-help">Ce montant s’ajoute à ce que le client vous doit. Choisissez le client plus bas.</p>
            } @else if (row.method === 'Cash') {
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

        @if (hasCredit()) {
          <fieldset class="mt-4 border-t border-sauge pt-4" aria-labelledby="client-credit-titre">
            <legend id="client-credit-titre" class="field-label">Client à crédit</legend>
            @if (customer(); as chosen) {
              <div class="rounded border border-sauge bg-ivoire p-3" role="status">
                <p class="text-lg font-semibold">{{ chosen.name }}</p>
                <p class="amount text-sm text-ardoise">
                  Dette : {{ format(chosen.debtFcfa) }} · Plafond : {{ format(chosen.creditCeilingFcfa) }} · Crédit disponible : {{ format(chosen.availableCreditFcfa) }}
                </p>
              </div>
              <button type="button" class="btn mt-2" (click)="customer.set(null)">Changer de client</button>
              @if (exceedsAsManager()) {
                <p class="mt-2 text-base font-semibold text-avertissement" role="note">
                  Ce crédit dépasse le plafond du client. En validant la vente, vous l’autorisez en tant que gérant ; cela sera noté dans le journal.
                </p>
              }
            } @else {
              <label class="field-label" for="client-recherche">Chercher un client (nom ou téléphone)</label>
              <input id="client-recherche" class="field" type="text" autocomplete="off" [value]="search()" (input)="searchCustomers($any($event.target).value)" />
              @if (customers.customers().length === 0) {
                <p class="field-help">
                  Aucun client enregistré sur cet appareil.
                  {{ network.online() ? 'Créez-le ci-dessous.' : 'Connectez-vous à internet pour charger la liste des clients ou en ajouter un.' }}
                </p>
              } @else {
                <ul class="mt-2 flex flex-col gap-2">
                  @for (candidate of customers.customers().slice(0, 6); track candidate.id) {
                    <li>
                      <button type="button" class="btn w-full !justify-start text-left" (click)="choose(candidate)">
                        <span class="min-w-0">
                          <span class="block font-semibold">{{ candidate.name }}</span>
                          <span class="amount block text-sm font-normal text-ardoise">Dette : {{ format(candidate.debtFcfa) }} · Crédit disponible : {{ format(candidate.availableCreditFcfa) }}</span>
                        </span>
                      </button>
                    </li>
                  }
                </ul>
              }
              @if (!creating()) {
                <button type="button" class="btn mt-3" (click)="creating.set(true)" [disabled]="!network.online()">Nouveau client</button>
                @if (!network.online()) {
                  <p class="field-help">Ajouter un client demande internet.</p>
                }
              } @else {
                <div class="mt-3 rounded border border-sauge p-3">
                  <label class="field-label" for="client-nom">Nom du client</label>
                  <input id="client-nom" class="field" type="text" autocomplete="off" [value]="newName()" (input)="newName.set($any($event.target).value)" />
                  <label class="field-label mt-3" for="client-telephone">Téléphone (facultatif)</label>
                  <input id="client-telephone" class="field" type="tel" inputmode="tel" autocomplete="off" [value]="newPhone()" (input)="newPhone.set($any($event.target).value)" />
                  @if (createError()) {
                    <p class="field-error" role="alert">{{ createError() }}</p>
                  }
                  <div class="mt-3 flex flex-wrap gap-2">
                    <button type="button" class="btn-primary" (click)="createCustomer()" [disabled]="creatingBusy()">Enregistrer le client</button>
                    <button type="button" class="btn" (click)="creating.set(false)">Annuler</button>
                  </div>
                </div>
              }
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
  protected readonly customers = inject(CustomerService);
  protected readonly network = inject(NetworkService);
  private readonly auth = inject(AuthService);

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
  protected readonly hasCredit = computed(() => this.rows().some((row) => row.method === 'Credit'));
  protected readonly customer = signal<CustomerSnapshot | null>(null);
  protected readonly search = signal('');
  protected readonly creating = signal(false);
  protected readonly creatingBusy = signal(false);
  protected readonly createError = signal('');
  protected readonly newName = signal('');
  protected readonly newPhone = signal('');

  /** Au-delà du plafond, seul un gérant passe (il valide en vendant) ; un caissier est arrêté avec les chiffres et ce qu'il faut faire. */
  private readonly canExceed = computed(() => ['Owner', 'Manager'].includes(this.auth.user()?.role ?? ''));
  protected readonly creditTotal = computed(() =>
    this.drafts().filter((draft) => draft.method === 'Credit').reduce((sum, draft) => sum + draft.amountFcfa, 0),
  );
  protected readonly exceedsAsManager = computed(() => {
    const chosen = this.customer();
    return chosen !== null && this.canExceed() && this.creditTotal() > chosen.availableCreditFcfa;
  });
  protected readonly errors = computed(() => {
    const chosen = this.customer();
    const problems = validatePayments(this.total(), this.drafts(), this.used(), this.hasCredit() ? chosen?.id : null);
    const ceiling = this.hasCredit() && chosen ? creditProblem(chosen, this.creditTotal(), this.canExceed()) : null;
    return ceiling ? [...problems, ceiling] : problems;
  });

  /** Récapitulatif d'une vente à crédit confirmée : combien, pour qui, et la nouvelle dette. */
  protected readonly soldOnCredit = computed(() => {
    const chosen = this.customer();
    const sale = this.confirmed();
    if (!chosen || !sale || !sale.payments.some((payment) => payment.method === 'Credit')) {
      return null;
    }
    const amount = sale.payments.filter((payment) => payment.method === 'Credit').reduce((sum, payment) => sum + payment.amountFcfa, 0);
    return { name: chosen.name, amount, newDebt: chosen.debtFcfa + amount };
  });

  async ngOnInit(): Promise<void> {
    this.rows.set([this.newRow('Cash', this.total())]);
    this.used.set(await this.cash.usedReferences());
    await this.customers.loadLocal();
    if (this.network.online()) {
      void this.customers.refresh();
    }
  }

  protected async searchCustomers(term: string): Promise<void> {
    this.search.set(term);
    await this.customers.loadLocal(term);
  }

  protected choose(candidate: CustomerSnapshot): void {
    this.customer.set(candidate);
    this.creating.set(false);
  }

  protected async createCustomer(): Promise<void> {
    if (this.creatingBusy()) {
      return;
    }
    if (this.newName().trim().length < 2) {
      this.createError.set('Donnez le nom du client (2 lettres au moins).');
      return;
    }
    this.createError.set('');
    this.creatingBusy.set(true);
    try {
      this.choose(await this.customers.create(this.newName(), this.newPhone()));
      this.newName.set('');
      this.newPhone.set('');
    } catch (error) {
      this.createError.set(onboardingError(error));
    } finally {
      this.creatingBusy.set(false);
    }
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
      this.confirmed.set(await this.cash.confirmSale(this.drafts(), this.hasCredit() ? (this.customer()?.id ?? null) : null));
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
