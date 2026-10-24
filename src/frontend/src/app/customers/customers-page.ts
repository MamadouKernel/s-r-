import { Component, computed, inject, OnDestroy, OnInit, signal } from '@angular/core';
import { formatFcfa, parseFcfa } from '../core/money';
import { CustomerSnapshot, MOBILE_MONEY_METHODS, PaymentMethod } from '../domain/types';
import { describeError } from '../management/errors';
import { canManageCustomers, canOperateCustomers, canSeeRepayments } from '../management/roles';
import { AuthService } from '../services/auth.service';
import { NetworkService } from '../services/network.service';
import { CustomersApi, Repayment } from './customers-api';

type Panel = { kind: 'repay' | 'edit' | 'history' | 'deactivate'; id: string };

/**
 * Clients et dettes (docs/specs/clients-credit, lot 2) : qui doit combien, encaisser un versement, ajouter ou corriger un client, voir
 * l'historique des versements. En ligne seulement : le serveur tient la dette. La caisse, elle, vend à crédit avec sa copie locale.
 */
@Component({
  selector: 'app-customers-page',
  template: `
    <section aria-labelledby="clients-titre">
      <div class="mb-4 flex flex-wrap items-center gap-3">
        <h2 id="clients-titre" class="text-xl font-bold">Clients</h2>
        @if (canOperate()) {
          <button type="button" class="btn-primary ml-auto" (click)="startAdd()" [disabled]="!network.online()">Ajouter un client</button>
        }
      </div>

      @if (notice()) {
        <p class="panel mb-4 font-semibold text-succes" role="status">{{ notice() }}</p>
      }
      @if (!network.online()) {
        <p class="panel mb-4 font-semibold text-avertissement" role="note">
          Vous êtes hors ligne : cet écran a besoin du réseau. La caisse, elle, continue de vendre à crédit avec la liste chargée plus tôt.
        </p>
      }

      @if (adding()) {
        <form class="panel mb-4" (submit)="add($event)" novalidate aria-labelledby="ajout-titre">
          <h3 id="ajout-titre" class="mb-2 text-lg font-bold">Nouveau client</h3>
          <label class="field-label" for="ajout-nom">Nom</label>
          <input id="ajout-nom" class="field" type="text" autocomplete="off" [value]="form().name" (input)="patch({ name: $any($event.target).value })" />
          <label class="field-label mt-3" for="ajout-telephone">Téléphone (facultatif)</label>
          <input id="ajout-telephone" class="field" type="tel" inputmode="tel" autocomplete="off" [value]="form().phone" (input)="patch({ phone: $any($event.target).value })" />
          @if (canManage()) {
            <label class="field-label mt-3" for="ajout-plafond">Plafond de crédit (FCFA, facultatif)</label>
            <input id="ajout-plafond" class="field amount text-right" type="text" inputmode="numeric" autocomplete="off" [value]="form().ceiling" (input)="patch({ ceiling: $any($event.target).value })" aria-describedby="ajout-plafond-aide" />
            <p id="ajout-plafond-aide" class="field-help">Laissez vide pour le plafond habituel de la boutique.</p>
          }
          @if (formError()) {
            <p class="field-error" role="alert">{{ formError() }}</p>
          }
          <div class="mt-4 flex flex-wrap gap-2">
            <button type="submit" class="btn-primary" [disabled]="busy()">Enregistrer le client</button>
            <button type="button" class="btn" (click)="adding.set(false)">Annuler</button>
          </div>
        </form>
      }

      <label class="field-label" for="clients-recherche">Chercher un client</label>
      <input id="clients-recherche" class="field mb-4" type="search" autocomplete="off" placeholder="Nom ou téléphone" [value]="search()" (input)="onSearch($any($event.target).value)" />

      @if (loading()) {
        <p class="panel text-ardoise" role="status">Chargement des clients…</p>
      } @else if (error()) {
        <div class="panel" role="alert">
          <p class="font-semibold text-erreur">{{ error() }}</p>
          <button type="button" class="btn mt-3" (click)="load()">Réessayer</button>
        </div>
      } @else if (customers().length === 0) {
        <p class="panel text-ardoise">
          {{ search() ? 'Aucun client ne correspond à cette recherche.' : 'Aucun client pour le moment. Ajoutez le premier avec « Ajouter un client », ou depuis la caisse en vendant à crédit.' }}
        </p>
      } @else {
        <p class="amount mb-3 text-base font-semibold" id="clients-total">
          Dettes à recouvrer : {{ format(totalDebt()) }}
          <span class="font-normal text-ardoise">chez {{ indebted() }} client{{ indebted() > 1 ? 's' : '' }}</span>
        </p>
        <ul class="flex flex-col gap-3">
          @for (customer of customers(); track customer.id) {
            <li class="panel">
              <div class="flex flex-wrap items-start gap-3">
                <div class="min-w-0 flex-1">
                  <p class="break-words text-lg font-semibold">{{ customer.name }}</p>
                  <p class="text-sm text-ardoise">{{ customer.phone ?? 'Pas de téléphone' }}</p>
                  <p class="amount mt-1 font-semibold" [class.text-erreur]="customer.debtFcfa > 0" [class.text-succes]="customer.debtFcfa === 0">
                    {{ customer.debtFcfa > 0 ? 'Doit ' + format(customer.debtFcfa) : 'À jour' }}
                  </p>
                  <p class="amount text-sm text-ardoise">Plafond {{ format(customer.creditCeilingFcfa) }} · Crédit disponible {{ format(customer.availableCreditFcfa) }}</p>
                </div>
                <div class="flex flex-wrap gap-2">
                  @if (canOperate() && customer.debtFcfa > 0) {
                    <button type="button" class="btn-primary" [attr.aria-label]="'Encaisser un versement de ' + customer.name" (click)="open('repay', customer)" [disabled]="!network.online()">Encaisser un versement</button>
                  }
                  @if (canSeeHistory()) {
                    <button type="button" class="btn" [attr.aria-label]="'Historique de ' + customer.name" (click)="openHistory(customer)">Historique</button>
                  }
                  @if (canManage()) {
                    <button type="button" class="btn" [attr.aria-label]="'Modifier ' + customer.name" (click)="open('edit', customer)" [disabled]="!network.online()">Modifier</button>
                    @if (customer.debtFcfa === 0) {
                      <button type="button" class="btn" [attr.aria-label]="'Désactiver ' + customer.name" (click)="open('deactivate', customer)" [disabled]="!network.online()">Désactiver</button>
                    }
                  }
                </div>
              </div>

              @if (panel(); as current) {
                @if (current.id === customer.id) {
                  <div class="mt-4 border-t border-sauge pt-4">
                    @switch (current.kind) {
                      @case ('repay') {
                        <form (submit)="repay($event, customer)" novalidate [attr.aria-label]="'Versement de ' + customer.name">
                          <label class="field-label" for="versement-montant">Montant versé (FCFA)</label>
                          <input id="versement-montant" class="field amount text-right" type="text" inputmode="numeric" autocomplete="off" [value]="form().amount" (input)="patch({ amount: $any($event.target).value })" aria-describedby="versement-aide" />
                          <p id="versement-aide" class="field-help">Au plus {{ format(customer.debtFcfa) }}, ce que le client doit. Un versement partiel est possible.</p>
                          <label class="field-label mt-3" for="versement-mode">Mode</label>
                          <select id="versement-mode" class="field" (change)="patch({ method: $any($event.target).value })">
                            <option value="Cash" [selected]="form().method === 'Cash'">Espèces</option>
                            @for (option of mobileMoney; track option.method) {
                              <option [value]="option.method" [selected]="form().method === option.method">{{ option.label }}</option>
                            }
                          </select>
                          @if (form().method !== 'Cash') {
                            <label class="field-label mt-3" for="versement-reference">Référence de la transaction</label>
                            <input id="versement-reference" class="field font-mono" type="text" autocomplete="off" autocapitalize="characters" spellcheck="false" [value]="form().reference" (input)="patch({ reference: $any($event.target).value })" />
                            <p class="field-help">Recopiez la référence du message de confirmation reçu par le client.</p>
                          }
                          @if (formError()) {
                            <p class="field-error" role="alert">{{ formError() }}</p>
                          }
                          <div class="mt-4 flex flex-wrap gap-2">
                            <button type="submit" class="btn-primary" [disabled]="busy()">Enregistrer le versement</button>
                            <button type="button" class="btn" (click)="panel.set(null)">Annuler</button>
                          </div>
                        </form>
                      }
                      @case ('edit') {
                        <form (submit)="save($event, customer)" novalidate [attr.aria-label]="'Modifier ' + customer.name">
                          <label class="field-label" for="edition-nom">Nom</label>
                          <input id="edition-nom" class="field" type="text" autocomplete="off" [value]="form().name" (input)="patch({ name: $any($event.target).value })" />
                          <label class="field-label mt-3" for="edition-telephone">Téléphone (facultatif)</label>
                          <input id="edition-telephone" class="field" type="tel" inputmode="tel" autocomplete="off" [value]="form().phone" (input)="patch({ phone: $any($event.target).value })" />
                          <label class="field-label mt-3" for="edition-plafond">Plafond de crédit (FCFA)</label>
                          <input id="edition-plafond" class="field amount text-right" type="text" inputmode="numeric" autocomplete="off" [value]="form().ceiling" (input)="patch({ ceiling: $any($event.target).value })" />
                          @if (formError()) {
                            <p class="field-error" role="alert">{{ formError() }}</p>
                          }
                          <div class="mt-4 flex flex-wrap gap-2">
                            <button type="submit" class="btn-primary" [disabled]="busy()">Enregistrer</button>
                            <button type="button" class="btn" (click)="panel.set(null)">Annuler</button>
                          </div>
                        </form>
                      }
                      @case ('deactivate') {
                        <div class="flex flex-wrap items-center gap-2">
                          <p class="w-full font-semibold text-erreur" role="alert">
                            Désactiver {{ customer.name }} ? Il ne sera plus proposé à la caisse. Son historique est conservé.
                          </p>
                          <button type="button" class="btn" (click)="panel.set(null)">Non, le garder</button>
                          <button type="button" class="btn-primary !border-erreur !bg-erreur" (click)="deactivate(customer)" [disabled]="busy()">Oui, désactiver</button>
                        </div>
                      }
                      @case ('history') {
                        @if (history().length === 0) {
                          <p class="text-ardoise">Aucun versement pour ce client.</p>
                        } @else {
                          <ul class="divide-y divide-sauge">
                            @for (item of history(); track item.id) {
                              <li class="flex flex-wrap items-baseline gap-x-3 py-2">
                                <span class="amount font-semibold">{{ format(item.amountFcfa) }}</span>
                                <span>{{ methodLabel(item.method) }}{{ item.reference ? ' · ' + item.reference : '' }}</span>
                                <span class="ml-auto text-sm text-ardoise">{{ date(item.at) }}</span>
                              </li>
                            }
                          </ul>
                        }
                        <button type="button" class="btn mt-2" (click)="panel.set(null)">Fermer</button>
                      }
                    }
                  </div>
                }
              }
            </li>
          }
        </ul>
      }
    </section>
  `,
})
export class CustomersPage implements OnInit, OnDestroy {
  private readonly api = inject(CustomersApi);
  private readonly auth = inject(AuthService);
  protected readonly network = inject(NetworkService);

  private readonly role = computed(() => this.auth.user()?.role);
  protected readonly canOperate = computed(() => canOperateCustomers(this.role()));
  protected readonly canManage = computed(() => canManageCustomers(this.role()));
  protected readonly canSeeHistory = computed(() => canSeeRepayments(this.role()));

  protected readonly customers = signal<CustomerSnapshot[]>([]);
  protected readonly loading = signal(true);
  protected readonly error = signal('');
  protected readonly notice = signal('');
  protected readonly busy = signal(false);
  protected readonly search = signal('');
  protected readonly adding = signal(false);
  protected readonly panel = signal<Panel | null>(null);
  protected readonly history = signal<Repayment[]>([]);
  protected readonly form = signal({ name: '', phone: '', ceiling: '', amount: '', method: 'Cash' as PaymentMethod, reference: '' });
  protected readonly formError = signal('');

  protected readonly totalDebt = computed(() => this.customers().reduce((sum, customer) => sum + customer.debtFcfa, 0));
  protected readonly indebted = computed(() => this.customers().filter((customer) => customer.debtFcfa > 0).length);

  protected readonly mobileMoney = MOBILE_MONEY_METHODS;
  protected format = formatFcfa;

  /** Identifiant de versement créé une fois par formulaire et renouvelé après un succès : un rejeu ne compte jamais deux fois. */
  private repaymentId = crypto.randomUUID();
  private timer: ReturnType<typeof setTimeout> | null = null;

  ngOnInit(): void {
    void this.load();
  }

  ngOnDestroy(): void {
    if (this.timer) {
      clearTimeout(this.timer);
    }
  }

  protected async load(): Promise<void> {
    this.loading.set(true);
    this.error.set('');
    try {
      this.customers.set(await this.api.list(this.search().trim() || null));
    } catch (error) {
      this.error.set(describeError(error));
    } finally {
      this.loading.set(false);
    }
  }

  protected onSearch(term: string): void {
    this.search.set(term);
    if (this.timer) {
      clearTimeout(this.timer);
    }
    this.timer = setTimeout(() => void this.load(), 250);
  }

  protected patch(change: Partial<ReturnType<typeof this.form>>): void {
    this.form.update((current) => ({ ...current, ...change }));
  }

  protected startAdd(): void {
    this.notice.set('');
    this.panel.set(null);
    this.formError.set('');
    this.form.set({ name: '', phone: '', ceiling: '', amount: '', method: 'Cash', reference: '' });
    this.adding.set(true);
  }

  protected async add(event: Event): Promise<void> {
    event.preventDefault();
    if (this.busy()) {
      return;
    }
    const name = this.form().name.trim();
    if (name.length < 2) {
      this.formError.set('Donnez le nom du client (2 lettres au moins).');
      return;
    }
    const ceilingText = this.form().ceiling.trim();
    const ceiling = ceilingText === '' ? null : parseFcfa(ceilingText);
    if (ceilingText !== '' && ceiling === null) {
      this.formError.set('Le plafond doit être un montant entier en FCFA.');
      return;
    }
    await this.run(async () => {
      await this.api.create({ customerId: crypto.randomUUID(), name, phone: this.form().phone.trim() || null, creditCeilingFcfa: this.canManage() ? ceiling : null });
      this.adding.set(false);
      this.notice.set(`Client ajouté : ${name}.`);
    });
  }

  protected open(kind: 'repay' | 'edit' | 'deactivate', customer: CustomerSnapshot): void {
    this.notice.set('');
    this.adding.set(false);
    this.formError.set('');
    this.form.set({
      name: customer.name, phone: customer.phone ?? '', ceiling: String(customer.creditCeilingFcfa), amount: '', method: 'Cash', reference: '',
    });
    this.panel.set({ kind, id: customer.id });
  }

  protected async openHistory(customer: CustomerSnapshot): Promise<void> {
    this.notice.set('');
    this.adding.set(false);
    this.history.set([]);
    this.panel.set({ kind: 'history', id: customer.id });
    try {
      this.history.set(await this.api.repayments(customer.id));
    } catch (error) {
      this.panel.set(null);
      this.error.set(describeError(error));
    }
  }

  protected async repay(event: Event, customer: CustomerSnapshot): Promise<void> {
    event.preventDefault();
    if (this.busy()) {
      return;
    }
    const amount = parseFcfa(this.form().amount);
    if (amount === null || amount <= 0) {
      this.formError.set('Saisissez le montant versé en chiffres entiers, supérieur à zéro.');
      return;
    }
    if (amount > customer.debtFcfa) {
      this.formError.set(`Ce montant dépasse ce que le client doit (${formatFcfa(customer.debtFcfa)}).`);
      return;
    }
    const method = this.form().method;
    if (method !== 'Cash' && this.form().reference.trim() === '') {
      this.formError.set('La référence de la transaction est obligatoire pour un paiement Mobile Money.');
      return;
    }
    this.formError.set('');
    this.busy.set(true);
    try {
      const done = await this.api.repay(customer.id, {
        repaymentId: this.repaymentId, amountFcfa: amount, method, reference: method === 'Cash' ? null : this.form().reference.trim(),
      });
      this.repaymentId = crypto.randomUUID();
      this.panel.set(null);
      this.notice.set(
        done.debtFcfa === 0
          ? `Versement enregistré. ${customer.name} ne doit plus rien.`
          : `Versement enregistré. Il reste ${formatFcfa(done.debtFcfa)} à payer à ${customer.name}.`,
      );
      await this.load();
    } catch (error) {
      this.formError.set(describeError(error));
    } finally {
      this.busy.set(false);
    }
  }

  protected async save(event: Event, customer: CustomerSnapshot): Promise<void> {
    event.preventDefault();
    if (this.busy()) {
      return;
    }
    const name = this.form().name.trim();
    const ceiling = parseFcfa(this.form().ceiling);
    if (name.length < 2) {
      this.formError.set('Donnez le nom du client (2 lettres au moins).');
      return;
    }
    if (ceiling === null) {
      this.formError.set('Le plafond doit être un montant entier en FCFA.');
      return;
    }
    this.formError.set('');
    this.busy.set(true);
    try {
      await this.api.update(customer.id, { name, phone: this.form().phone.trim() || null, creditCeilingFcfa: ceiling });
      this.panel.set(null);
      this.notice.set(`Client modifié : ${name}.`);
      await this.load();
    } catch (error) {
      this.formError.set(describeError(error));
    } finally {
      this.busy.set(false);
    }
  }

  protected async deactivate(customer: CustomerSnapshot): Promise<void> {
    await this.run(async () => {
      await this.api.deactivate(customer.id);
      this.panel.set(null);
      this.notice.set(`Client désactivé : ${customer.name}.`);
    });
  }

  protected methodLabel(method: PaymentMethod): string {
    return method === 'Cash' ? 'Espèces' : (MOBILE_MONEY_METHODS.find((option) => option.method === method)?.label ?? method);
  }

  protected date(iso: string): string {
    return new Date(iso).toLocaleDateString('fr-FR');
  }

  private async run(action: () => Promise<void>): Promise<void> {
    this.busy.set(true);
    this.formError.set('');
    try {
      await action();
      await this.load();
    } catch (error) {
      this.formError.set(describeError(error));
      this.panel.update((current) => current);
    } finally {
      this.busy.set(false);
    }
  }
}
