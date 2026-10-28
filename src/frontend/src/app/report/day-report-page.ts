import { Component, inject, OnInit, signal } from '@angular/core';
import { formatFcfa } from '../core/money';
import { formatQuantity } from '../core/measure';
import { MOBILE_MONEY_METHODS, PaymentMethod } from '../domain/types';
import { NetworkService } from '../services/network.service';
import { ApiError } from '../sync/sync-api';
import { DayCashier, DayReport, DayReportApi, DaySession } from './day-report-api';

const MOVEMENT_LABEL = { Expense: 'Dépenses', Drop: 'Versements au coffre', Topup: 'Apports de monnaie' } as const;

const today = () => new Date().toISOString().slice(0, 10);

/**
 * Le rapport d'une journée (docs/specs/cloture-sorties-rapport CLO-11) : ce que le patron lirait le soir, mais à l'écran. Il donne les chiffres tels
 * quels, sans jamais en tirer une retenue ni un reproche : c'est au patron de juger.
 */
@Component({
  selector: 'app-day-report-page',
  template: `
    <section class="mx-auto max-w-2xl" aria-labelledby="rapport-titre">
      <h2 id="rapport-titre" class="mb-2 text-xl font-bold">Rapport du jour</h2>

      @if (!network.online()) {
        <p class="panel text-ardoise" role="status">Cet écran a besoin du réseau : il lit les chiffres sur le serveur. Reconnectez-vous puis revenez ici.</p>
      } @else {
        <label class="field-label" for="jour">Jour</label>
        <input id="jour" class="field mb-4 max-w-48" type="date" [value]="day()" [max]="maxDay" (change)="choose($any($event.target).value)" />

        @if (error()) {
          <p class="field-error" role="alert">{{ error() }}</p>
          <button type="button" class="btn mt-2" (click)="load()">Réessayer</button>
        } @else if (loading()) {
          <p class="text-ardoise" role="status">Lecture du rapport…</p>
        } @else if (report(); as r) {
          @if (!hasActivity(r)) {
            <p class="panel text-ardoise">Aucune vente ce jour-là, et aucune caisse ni aucun mouvement d’argent.</p>
          } @else {
            @if (toClose(r).length > 0) {
              <section class="panel mb-3 border-avertissement" aria-labelledby="aclore-titre">
                <h3 id="aclore-titre" class="text-lg font-bold text-avertissement">À clôturer</h3>
                <ul class="mt-1 space-y-1">
                  @for (session of toClose(r); track session.id) {
                    <li>La caisse de {{ name(r, session.cashierId) }} est ouverte depuis le {{ date(session.openedAt) }}.
                      @if (session.drawerAboveWarning) { Il y a beaucoup d’argent dans son tiroir : faites un versement au coffre. }
                    </li>
                  }
                </ul>
              </section>
            }

            <section class="panel mb-3" aria-labelledby="ventes-titre">
              <h3 id="ventes-titre" class="text-lg font-bold">Ventes</h3>
              <p class="amount text-2xl font-bold">{{ money(r.netTotalFcfa) }}</p>
              <p class="text-ardoise">{{ r.salesCount }} {{ r.salesCount > 1 ? 'tickets' : 'ticket' }}</p>
              <p class="text-ardoise">Panier moyen : {{ money(r.averageBasketFcfa) }}</p>
              <ul class="mt-2 divide-y divide-sauge">
                @for (payment of cashAndOthers(r); track payment.method) {
                  <li class="flex justify-between gap-2 py-1"><span>{{ methodLabel(payment.method) }} : {{ money(payment.amountFcfa) }}</span><span class="text-sm text-ardoise">{{ payment.count }} paiement{{ payment.count > 1 ? 's' : '' }}</span></li>
                }
              </ul>
              @if (mobileMoney(r).length > 0) {
                <p class="mt-2 text-sm text-ardoise">À retrouver sur votre téléphone :</p>
                <ul class="divide-y divide-sauge">
                  @for (payment of mobileMoney(r); track payment.method) {
                    <li class="flex justify-between gap-2 py-1"><span>{{ methodLabel(payment.method) }} : {{ money(payment.amountFcfa) }}</span><span class="text-sm text-ardoise">{{ payment.count }} paiement{{ payment.count > 1 ? 's' : '' }}</span></li>
                  }
                </ul>
              }
            </section>

            @if (r.cashiers.length > 0) {
              <section class="panel mb-3" aria-labelledby="caissiers-titre">
                <h3 id="caissiers-titre" class="text-lg font-bold">Par caissier</h3>
                <ul class="divide-y divide-sauge">
                  @for (cashier of r.cashiers; track cashier.cashierId) {
                    <li class="py-2">
                      <p class="font-semibold">{{ name(r, cashier.cashierId) }} · {{ money(cashier.netTotalFcfa) }} ({{ cashier.salesCount }} {{ cashier.salesCount > 1 ? 'tickets' : 'ticket' }})</p>
                      <p class="text-sm text-ardoise">{{ figures(cashier) }}</p>
                      <p class="text-sm font-semibold" [class.text-erreur]="cashier.varianceFcfa !== 0">{{ gap(cashier) }}</p>
                    </li>
                  }
                </ul>
              </section>
            }

            @if (r.sessions.length > 0) {
              <section class="panel mb-3" aria-labelledby="caisses-titre">
                <h3 id="caisses-titre" class="text-lg font-bold">Caisses</h3>
                <ul class="divide-y divide-sauge">
                  @for (session of r.sessions; track session.id) {
                    <li class="py-2">
                      <p class="font-semibold">{{ name(r, session.cashierId) }}</p>
                      <p class="text-sm text-ardoise">
                        Ouverte à {{ time(session.openedAt) }} avec {{ money(session.openingFloatFcfa) }}
                        @if (session.closedAt) { · clôturée à {{ time(session.closedAt) }}, comptée {{ money(session.countedCashFcfa ?? 0) }} } @else { · pas encore clôturée }
                      </p>
                      @if (session.varianceFcfa !== null && session.varianceFcfa !== 0) {
                        <p class="text-sm font-semibold text-erreur">{{ sessionGap(session) }}</p>
                      }
                    </li>
                  }
                </ul>
              </section>
            }

            @if (r.movements.length > 0) {
              <section class="panel mb-3" aria-labelledby="mouvements-titre">
                <h3 id="mouvements-titre" class="text-lg font-bold">Argent sorti ou entré du tiroir</h3>
                <ul class="divide-y divide-sauge">
                  @for (movement of r.movements; track $index) {
                    <li class="py-1">{{ movementLabel(movement) }}</li>
                  }
                </ul>
              </section>
            }

            @if (r.debtCreatedFcfa > 0 || r.repaymentsFcfa > 0) {
              <section class="panel mb-3" aria-labelledby="credit-titre">
                <h3 id="credit-titre" class="text-lg font-bold">Crédit</h3>
                <p>dettes créées {{ money(r.debtCreatedFcfa) }} · remboursements reçus {{ money(r.repaymentsFcfa) }}</p>
              </section>
            }

            @if (r.lowStock.length > 0) {
              <section class="panel mb-3" aria-labelledby="stock-titre">
                <h3 id="stock-titre" class="text-lg font-bold">Stock à surveiller</h3>
                <ul class="divide-y divide-sauge">
                  @for (line of r.lowStock; track line.name) {
                    <li class="py-1">{{ line.name }} : {{ quantity(line.quantity, line) }} (seuil {{ quantity(line.threshold, line) }})</li>
                  }
                </ul>
              </section>
            }
          }
        }
      }
    </section>
  `,
})
export class DayReportPage implements OnInit {
  private readonly api = inject(DayReportApi);
  protected readonly network = inject(NetworkService);

  protected readonly maxDay = today();
  protected readonly day = signal(today());
  protected readonly report = signal<DayReport | null>(null);
  protected readonly loading = signal(false);
  protected readonly error = signal('');

  async ngOnInit(): Promise<void> {
    if (this.network.online()) {
      await this.load();
    }
  }

  protected async choose(value: string): Promise<void> {
    if (value) {
      this.day.set(value);
      await this.load();
    }
  }

  protected async load(): Promise<void> {
    this.loading.set(true);
    this.error.set('');
    try {
      this.report.set(await this.api.day(this.day()));
    } catch (error) {
      this.report.set(null);
      this.error.set(error instanceof ApiError && error.status === 0 ? 'Le serveur ne répond pas. Vérifiez le réseau puis réessayez.' : error instanceof Error ? error.message : 'Le rapport n’a pas pu être lu.');
    } finally {
      this.loading.set(false);
    }
  }

  protected money = formatFcfa;

  protected hasActivity(r: DayReport): boolean {
    return r.salesCount > 0 || r.movements.length > 0 || r.sessions.length > 0;
  }

  protected toClose(r: DayReport): DaySession[] {
    return r.sessions.filter((session) => session.needsClosing || session.drawerAboveWarning);
  }

  protected name(r: DayReport, id: string): string {
    return r.names[id] ?? 'un caissier';
  }

  protected methodLabel(method: PaymentMethod): string {
    return method === 'Cash' ? 'Espèces' : method === 'Credit' ? 'Crédit' : (MOBILE_MONEY_METHODS.find((item) => item.method === method)?.label ?? method);
  }

  protected cashAndOthers(r: DayReport) {
    return r.payments.filter((payment) => !MOBILE_MONEY_METHODS.some((item) => item.method === payment.method));
  }

  protected mobileMoney(r: DayReport) {
    return r.payments.filter((payment) => MOBILE_MONEY_METHODS.some((item) => item.method === payment.method));
  }

  protected figures(c: DayCashier): string {
    return `remises ${c.discountsCount} (${formatFcfa(c.discountFcfa)}) · annulations ${c.cancellationsCount} (${formatFcfa(c.cancelledFcfa)}) · retours ${c.returnsCount} (${formatFcfa(c.returnedFcfa)})`;
  }

  protected gap(c: DayCashier): string {
    return c.varianceFcfa === 0 ? 'écart de caisse : aucun' : `écart de caisse : ${c.varianceFcfa < 0 ? 'manque' : 'excédent'} de ${formatFcfa(Math.abs(c.varianceFcfa))}`;
  }

  protected sessionGap(session: DaySession): string {
    const variance = session.varianceFcfa ?? 0;
    return `${variance < 0 ? 'Manque' : 'Excédent'} de ${formatFcfa(Math.abs(variance))}`;
  }

  protected movementLabel(m: DayReport['movements'][number]): string {
    const reason = m.reason ? ` (${m.reason})` : '';
    return `${MOVEMENT_LABEL[m.kind]}${reason} : ${formatFcfa(m.amountFcfa)}${m.count > 1 ? ` en ${m.count} fois` : ''}`;
  }

  protected quantity(value: number, line: DayReport['lowStock'][number]): string {
    return formatQuantity(value, line.unit);
  }

  protected date(iso: string): string {
    return new Date(iso).toLocaleDateString('fr-FR', { day: '2-digit', month: '2-digit' });
  }

  protected time(iso: string): string {
    return new Date(iso).toLocaleTimeString('fr-FR', { hour: '2-digit', minute: '2-digit' });
  }
}
