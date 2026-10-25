import { Component, computed, inject, OnInit, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { formatFcfa, parseFcfa } from '../core/money';
import { describeError } from '../management/errors';
import { AuthService } from '../services/auth.service';
import { NetworkService } from '../services/network.service';
import { PaymentStatus, SubscriptionApi, SubscriptionMethod, SubscriptionPayment, SubscriptionPeriod } from './subscription-api';
import { SubscriptionService } from './subscription.service';

const METHODS: Record<SubscriptionMethod, string> = {
  Wave: 'Wave',
  OrangeMoney: 'Orange Money',
  MtnMomo: 'MTN MoMo',
  MoovMoney: 'Moov Money',
};

function fullDate(iso: string): string {
  const date = new Date(iso);
  return `${String(date.getDate()).padStart(2, '0')}/${String(date.getMonth() + 1).padStart(2, '0')}/${date.getFullYear()}`;
}

function shortDate(iso: string): string {
  return fullDate(iso).slice(0, 5);
}

/**
 * « Mon abonnement » (docs/specs/abonnement ABO-09) : où j'en suis, comment payer en trois gestes, déclarer mon paiement, mon historique.
 * Le propriétaire déclare ; le gérant lit. Un paiement déclaré n'active rien tant que l'équipe ne l'a pas validé.
 */
@Component({
  selector: 'app-subscription-page',
  imports: [RouterLink],
  template: `
    <section class="mx-auto max-w-2xl" aria-labelledby="abo-titre">
      <h2 id="abo-titre" class="mb-4 text-xl font-bold">Mon abonnement</h2>

      @if (!network.online()) {
        <p class="panel mb-4 font-semibold text-avertissement" role="note">
          Vous êtes hors ligne : cet écran a besoin du réseau. La caisse, elle, continue de fonctionner.
        </p>
      }

      @if (subscription.view(); as view) {
        <div class="panel">
          <p class="flex flex-wrap items-baseline gap-x-3 gap-y-1">
            <span class="text-2xl font-bold">{{ stateTitle() }}</span>
            <span class="font-semibold" [class.text-feuille]="view.status === 'Active'" [class.text-avertissement]="view.status === 'Trial' || view.status === 'Grace'" [class.text-erreur]="view.status === 'ReadOnly'">{{ stateTag() }}</span>
          </p>
          <p class="mt-2 text-base text-ardoise">{{ stateText() }}</p>
        </div>

        @if (view.monthlyPriceFcfa !== null && view.payNumbers) {
          @if (isOwner()) {
            <div class="panel mt-3">
              <h3 class="text-lg font-bold">1. Choisissez la durée</h3>
              <div class="mt-2 grid gap-2 sm:grid-cols-2" role="group" aria-label="Durée de l’abonnement">
                @for (option of periods(); track option.period) {
                  <button type="button" class="btn !h-auto flex-col items-start !py-3 text-left" [class.border-2]="period() === option.period" [class.border-feuille]="period() === option.period" [class.bg-feuille/10]="period() === option.period" [attr.aria-pressed]="period() === option.period" (click)="choosePeriod(option.period)">
                    <span class="text-lg font-bold">{{ option.label }}</span>
                    <span class="amount text-base font-normal">{{ format(option.price) }}{{ option.bonus ? ' · ' + option.bonus : '' }}</span>
                  </button>
                }
              </div>
            </div>

            <div class="panel mt-3">
              <h3 class="text-lg font-bold">2. Payez avec votre téléphone</h3>
              <p class="mt-2 font-semibold">a. Choisissez votre moyen de paiement</p>
              <div class="mt-2 flex flex-wrap gap-2" role="group" aria-label="Moyen de paiement">
                @for (option of methods(); track option.method) {
                  <button type="button" class="btn" [class.border-2]="method() === option.method" [class.border-feuille]="method() === option.method" [attr.aria-pressed]="method() === option.method" (click)="method.set(option.method)">{{ option.label }}</button>
                }
              </div>
              <p class="mt-4 font-semibold">b. Envoyez <span class="amount text-xl">{{ format(price()) }}</span> par {{ methodLabel() }} au numéro :</p>
              <p class="mt-2 rounded border border-dashed border-ardoise bg-ivoire p-3 text-xl font-bold amount">{{ number() }} <span class="text-sm font-normal text-ardoise">· Sôrô</span></p>
              <p class="mt-4 font-semibold">c. Notez la référence de la transaction</p>
              <p class="text-base text-ardoise">C’est le code reçu par message après le paiement.</p>
            </div>

            <form class="panel mt-3" (submit)="declare($event)" novalidate>
              <h3 class="text-lg font-bold">3. Déclarez votre paiement</h3>
              <label class="field-label mt-2" for="reference">Référence de la transaction</label>
              <input id="reference" class="field" type="text" maxlength="100" autocomplete="off" placeholder="Exemple : W-123456 ou MP2610.1234" [value]="reference()" (input)="reference.set($any($event.target).value)" [attr.aria-invalid]="referenceError() ? 'true' : null" aria-describedby="reference-erreur" />
              <p id="reference-erreur" class="field-error">{{ referenceError() }}</p>
              <label class="field-label mt-3" for="montant">Montant envoyé (FCFA)</label>
              <input id="montant" class="field amount text-right" type="text" inputmode="numeric" autocomplete="off" [value]="amount()" (input)="amount.set($any($event.target).value)" [attr.aria-invalid]="amountError() ? 'true' : null" aria-describedby="montant-erreur" />
              <p id="montant-erreur" class="field-error">{{ amountError() }}</p>
              <p class="field-help">Nous vérifions votre paiement sur notre relevé, en général dans la journée, et vous écrivons dès que c’est fait. Tant que ce n’est pas validé, rien ne change pour votre boutique.</p>
              @if (failure()) {
                <p class="field-error mt-3" role="alert">{{ failure() }}</p>
              }
              @if (sent()) {
                <p class="mt-3 border-l-4 border-feuille bg-feuille/10 p-3" role="status">
                  <strong>Déclaration envoyée.</strong> Nous la vérifions et vous écrivons dès que c’est fait. Votre boutique continue de fonctionner normalement.
                </p>
              }
              <button type="submit" class="btn-primary mt-3 w-full" [disabled]="saving()">{{ saving() ? 'Envoi…' : 'J’ai payé : envoyer ma déclaration' }}</button>
            </form>
          } @else {
            <p class="panel mt-3 text-base text-ardoise">
              Seul le propriétaire peut déclarer un paiement. Prix : {{ format(view.monthlyPriceFcfa) }} par mois{{ view.annualPriceFcfa ? ', ' + format(view.annualPriceFcfa) + ' par an' : '' }}.
            </p>
          }
        }

        @if (view.payments; as payments) {
          <div class="panel mt-3">
            <h3 class="text-lg font-bold">Mes paiements</h3>
            @if (payments.length === 0) {
              <p class="mt-2 text-base text-ardoise">Aucun paiement pour le moment.</p>
            } @else {
              <div class="relative mt-2 overflow-x-auto">
                <table class="w-full text-left text-base">
                  <caption class="sr-only">Historique des paiements d’abonnement</caption>
                  <thead class="text-sm text-ardoise">
                    <tr>
                      <th scope="col" class="px-2 py-2">Date</th>
                      <th scope="col" class="px-2 py-2">Durée</th>
                      <th scope="col" class="px-2 py-2">Moyen</th>
                      <th scope="col" class="px-2 py-2">Référence</th>
                      <th scope="col" class="px-2 py-2 text-right">Montant</th>
                      <th scope="col" class="px-2 py-2">État</th>
                    </tr>
                  </thead>
                  <tbody class="divide-y divide-sauge">
                    @for (payment of payments; track payment.id) {
                      <tr>
                        <td class="px-2 py-2">{{ shortDate(payment.declaredAt) }}</td>
                        <td class="px-2 py-2">{{ periodLabel(payment.period) }}</td>
                        <td class="px-2 py-2">{{ methodName(payment.method) }}</td>
                        <td class="px-2 py-2">{{ payment.reference }}</td>
                        <td class="amount px-2 py-2 text-right">{{ format(payment.amountFcfa) }}</td>
                        <td class="px-2 py-2">
                          <span class="font-semibold" [class.text-feuille]="payment.status === 'Validated'" [class.text-avertissement]="payment.status === 'Pending'" [class.text-erreur]="payment.status === 'Refused'">{{ statusText(payment) }}</span>
                          @if (payment.status === 'Refused' && payment.reason) {
                            <span class="block text-sm text-ardoise">{{ payment.reason }}</span>
                          }
                        </td>
                      </tr>
                    }
                  </tbody>
                </table>
              </div>
            }
          </div>
        }
      } @else if (loading()) {
        <p class="panel text-ardoise" role="status">Chargement de votre abonnement…</p>
      } @else {
        <div class="panel" role="alert">
          <p class="font-semibold text-erreur">Impossible de lire votre abonnement pour le moment. Vérifiez la connexion puis réessayez.</p>
          <button type="button" class="btn mt-3" (click)="load()">Réessayer</button>
        </div>
      }

      <a class="btn mt-4" routerLink="/gestion/produits">← Retour à la gestion</a>
    </section>
  `,
})
export class SubscriptionPage implements OnInit {
  private readonly api = inject(SubscriptionApi);
  private readonly auth = inject(AuthService);
  protected readonly subscription = inject(SubscriptionService);
  protected readonly network = inject(NetworkService);

  /** Identifiant fixé à l'ouverture du formulaire : un nouvel essai après une coupure ne déclare pas deux fois le même paiement. */
  private paymentId = crypto.randomUUID();

  protected readonly loading = signal(true);
  protected readonly period = signal<SubscriptionPeriod>('Monthly');
  protected readonly method = signal<SubscriptionMethod>('Wave');
  protected readonly reference = signal('');
  protected readonly amount = signal('');
  protected readonly referenceError = signal('');
  protected readonly amountError = signal('');
  protected readonly failure = signal('');
  protected readonly saving = signal(false);
  protected readonly sent = signal(false);

  protected readonly isOwner = computed(() => this.auth.user()?.role === 'Owner');
  protected format = formatFcfa;
  protected shortDate = shortDate;

  protected readonly price = computed(() => {
    const view = this.subscription.view();
    return (this.period() === 'Annual' ? view?.annualPriceFcfa : view?.monthlyPriceFcfa) ?? 0;
  });

  protected readonly periods = computed(() => {
    const view = this.subscription.view();
    const monthly = view?.monthlyPriceFcfa ?? 0;
    const annual = view?.annualPriceFcfa ?? 0;
    const free = monthly > 0 ? Math.round((12 * monthly - annual) / monthly) : 0;
    return [
      { period: 'Monthly' as const, label: '1 mois', price: monthly, bonus: '' },
      { period: 'Annual' as const, label: '12 mois', price: annual, bonus: free > 0 ? `${free} mois offerts` : '' },
    ];
  });

  protected readonly methods = computed(() =>
    (Object.keys(METHODS) as SubscriptionMethod[])
      .filter((method) => !!this.subscription.view()?.payNumbers?.[method])
      .map((method) => ({ method, label: METHODS[method] })),
  );
  protected readonly methodLabel = computed(() => METHODS[this.method()]);
  protected readonly number = computed(() => this.subscription.view()?.payNumbers?.[this.method()] ?? '');

  protected readonly stateTitle = computed(() => {
    switch (this.subscription.view()?.status) {
      case 'Trial':
        return 'Essai gratuit';
      case 'Active':
        return 'Abonnement actif';
      case 'Grace':
        return 'Abonnement échu';
      default:
        return 'Lecture seule';
    }
  });

  protected readonly stateTag = computed(() => {
    const view = this.subscription.view();
    const days = `${view?.daysLeft ?? 0} jour${(view?.daysLeft ?? 0) > 1 ? 's' : ''}`;
    switch (view?.status) {
      case 'Trial':
        return `Plus que ${days}`;
      case 'Active':
        return `Jusqu’au ${fullDate(view.endsAt)}`;
      case 'Grace':
        return `Plus que ${days} avant la lecture seule`;
      default:
        return 'Vos données sont conservées';
    }
  });

  protected readonly stateText = computed(() => {
    const view = this.subscription.view();
    if (!view) {
      return '';
    }
    switch (view.status) {
      case 'Trial':
        return `Votre essai se termine le ${fullDate(view.endsAt)}. Si vous payez maintenant, vos jours restants ne sont pas perdus : la période payée commence après.`;
      case 'Active':
        return `Votre abonnement est payé jusqu’au ${fullDate(view.endsAt)}. Vous pouvez payer à l’avance : la nouvelle période s’ajoute à la suite.`;
      case 'Grace':
        return `L’échéance du ${fullDate(view.endsAt)} est passée. Tout fonctionne encore jusqu’au ${fullDate(view.readOnlyFrom)} ; ensuite la boutique passera en lecture seule.`;
      default:
        return 'La boutique est en lecture seule : vous pouvez tout consulter, mais plus vendre ni modifier. Rien n’est perdu. Dès que votre paiement est validé, tout reprend.';
    }
  });

  async ngOnInit(): Promise<void> {
    await this.load();
  }

  protected async load(): Promise<void> {
    this.loading.set(true);
    await this.subscription.refresh();
    this.loading.set(false);
    const first = this.methods()[0];
    if (first && !this.methods().some((option) => option.method === this.method())) {
      this.method.set(first.method);
    }
    this.amount.set(String(this.price()));
  }

  protected choosePeriod(period: SubscriptionPeriod): void {
    this.period.set(period);
    this.amount.set(String(this.price()));
  }

  protected methodName(method: SubscriptionMethod): string {
    return METHODS[method];
  }

  protected periodLabel(period: SubscriptionPeriod): string {
    return period === 'Annual' ? '12 mois' : '1 mois';
  }

  protected statusText(payment: SubscriptionPayment): string {
    const labels: Record<PaymentStatus, string> = {
      Pending: 'En attente de validation',
      Validated: payment.coveredTo ? `Validé · couvre jusqu’au ${shortDate(payment.coveredTo)}` : 'Validé',
      Refused: 'Refusé',
    };
    return labels[payment.status];
  }

  protected async declare(event: Event): Promise<void> {
    event.preventDefault();
    if (this.saving()) {
      return;
    }
    this.failure.set('');
    this.sent.set(false);
    const reference = this.reference().trim();
    const amount = parseFcfa(this.amount());
    this.referenceError.set(reference === '' ? 'La référence de la transaction est obligatoire.' : '');
    const periodName = this.period() === 'Annual' ? 'annuel' : 'mensuel';
    this.amountError.set(
      amount === null
        ? 'Saisissez le montant en chiffres, sans virgule.'
        : amount < this.price()
          ? `Le montant est trop bas : l’abonnement ${periodName} coûte ${formatFcfa(this.price())}.`
          : '',
    );
    if (this.referenceError() || this.amountError() || amount === null) {
      return;
    }

    this.saving.set(true);
    try {
      await this.api.declare({ paymentId: this.paymentId, period: this.period(), method: this.method(), reference, amountFcfa: amount });
      this.sent.set(true);
      this.reference.set('');
      this.paymentId = crypto.randomUUID();
      await this.subscription.refresh();
    } catch (error) {
      this.failure.set(describeError(error));
    } finally {
      this.saving.set(false);
    }
  }
}
