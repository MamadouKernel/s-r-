import { Component, computed, inject, OnDestroy, OnInit, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { formatFcfa } from '../core/money';
import { describeVariance } from '../core/variance';
import { describeError } from '../management/errors';
import { ProductsApi } from '../management/products-api';
import { canManageStock, canSeeMargin } from '../management/roles';
import { TeamApi } from '../management/team-api';
import { AuthService } from '../services/auth.service';
import { NetworkService } from '../services/network.service';
import { ActivityReport, DashboardApi, ProductRanking, SessionSummary } from './dashboard-api';
import { averageBasket, describeVariation, hoursOpen, isStaleSession, paymentLabel, paymentShares, RankingCriterionName, variation } from './metrics';
import { lastDaysRange, startOfLocalDay, todayRange, yesterdaySameTimeRange } from './period';

const REFRESH_EVERY_MS = 60_000;
const RANKING_SIZE = 5;

const CRITERIA: readonly { value: RankingCriterionName; label: string }[] = [
  { value: 'quantity', label: 'Plus vendus' },
  { value: 'revenue', label: 'Plus de chiffre d’affaires' },
  { value: 'margin', label: 'Plus rentables' },
];

const PERIODS: readonly { days: number; label: string }[] = [
  { days: 1, label: 'Aujourd’hui' },
  { days: 7, label: '7 jours' },
  { days: 30, label: '30 jours' },
];

/** Un bloc de données lu séparément : un échec n'efface ni ne bloque les autres, et ses derniers chiffres restent visibles. */
interface Block<T> {
  data: T | null;
  at: Date | null;
  error: string;
}

const empty = <T>(): Block<T> => ({ data: null, at: null, error: '' });

interface ActivityBlock {
  today: ActivityReport;
  yesterday: ActivityReport;
}

interface SessionsBlock {
  open: SessionSummary[];
  variances: SessionSummary[];
}

/** TDB-01 à TDB-07 : le tableau de bord du propriétaire, pensé pour un téléphone. En ligne seulement. */
@Component({
  selector: 'app-dashboard-page',
  imports: [RouterLink],
  template: `
    <section aria-labelledby="tdb-titre">
      <div class="mb-3 flex flex-wrap items-end gap-x-4 gap-y-2">
        <div class="min-w-0 flex-1">
          <h2 id="tdb-titre" class="text-xl font-bold">Aujourd’hui</h2>
          <!-- Seule la première lettre prend une majuscule : « Vendredi 23 octobre », pas « Octobre ». -->
          <p class="text-base text-ardoise first-letter:uppercase">{{ dateLabel() }}</p>
        </div>
        <button type="button" class="btn-primary" (click)="refresh()" [disabled]="refreshing()">
          {{ refreshing() ? 'Actualisation…' : 'Actualiser' }}
        </button>
      </div>

      <p class="mb-3 text-sm text-ardoise">{{ updatedLabel() }}</p>
      <p class="sr-only" role="status" aria-live="polite">{{ announcement() }}</p>

      @if (!network.online()) {
        <p class="panel mb-3 font-semibold text-avertissement" role="note">Vous êtes hors ligne : les chiffres affichés ne sont pas à jour.</p>
      }
      @if (staleWarning()) {
        <p class="panel mb-3 font-semibold text-avertissement" role="alert">{{ staleWarning() }}</p>
      }

      @if (firstLoad()) {
        <p class="panel text-ardoise" role="status">Chargement du tableau de bord…</p>
      } @else {
        <!-- Chiffre d'affaires : le chiffre qui compte, en grand, puis des lignes lisibles -->
        <section class="panel mb-3" aria-labelledby="ca-titre">
          <h3 id="ca-titre" class="text-base font-semibold text-ardoise">Chiffre d’affaires du jour</h3>
          @if (activity().data; as data) {
            <p class="amount text-4xl font-bold">{{ money(data.today.netTotalFcfa) }}</p>
            <p class="mt-1 text-base font-semibold" [class.text-succes]="trend().kind === 'up'" [class.text-erreur]="trend().kind === 'down'">
              {{ trendText() }}
            </p>
            @if (data.today.salesCount === 0) {
              <p class="mt-2 text-base text-ardoise">Aucune vente pour le moment aujourd’hui.</p>
            }
            <dl class="mt-3 grid grid-cols-[1fr_auto] gap-x-4 gap-y-1 border-t border-sauge pt-3 text-base">
              <dt>Tickets</dt>
              <dd class="amount text-right font-bold">{{ data.today.salesCount }}</dd>
              <dt class="text-sm text-ardoise">Hier à la même heure</dt>
              <dd class="amount text-right text-sm text-ardoise">{{ data.yesterday.salesCount }} ticket{{ data.yesterday.salesCount > 1 ? 's' : '' }} · {{ money(data.yesterday.netTotalFcfa) }}</dd>
              <dt>Panier moyen</dt>
              <dd class="amount text-right font-bold">{{ basket() === null ? '—' : money(basket()!) }}</dd>
              <dt>Remises accordées</dt>
              <dd class="amount text-right font-bold">{{ money(data.today.discountTotalFcfa) }}</dd>
              <dt>Ventes annulées</dt>
              <dd class="amount text-right font-bold">{{ data.today.cancelledCount }} · {{ money(data.today.cancelledTotalFcfa) }}</dd>
            </dl>
          } @else {
            <p class="field-error" role="alert">{{ activity().error }}</p>
            <button type="button" class="btn mt-2" (click)="refresh()">Réessayer</button>
          }
        </section>

        @if (activity().data; as data) {
          <section class="panel mb-3" aria-labelledby="paiements-titre">
            <h3 id="paiements-titre" class="text-lg font-bold">Répartition des paiements</h3>
            @if (shares().length === 0) {
              <p class="mt-1 text-base text-ardoise">Aucun paiement enregistré aujourd’hui.</p>
            } @else {
              <ul class="mt-2 divide-y divide-sauge">
                @for (share of shares(); track share.method) {
                  <li class="py-2">
                    <div class="flex items-baseline justify-between gap-2">
                      <span class="font-semibold">{{ payment(share.method) }}</span>
                      <span class="amount font-bold">{{ money(share.amountFcfa) }}</span>
                    </div>
                    <div class="mt-1 flex items-center gap-2">
                      <div class="h-2 flex-1 rounded bg-sauge" aria-hidden="true">
                        <div class="h-2 rounded bg-feuille" [style.width.%]="share.percent"></div>
                      </div>
                      <span class="amount w-28 text-right text-sm text-ardoise">{{ share.percent }} % · {{ share.count }} paiement{{ share.count > 1 ? 's' : '' }}</span>
                    </div>
                  </li>
                }
              </ul>
            }
          </section>

          <section class="panel mb-3" aria-labelledby="caissiers-titre">
            <h3 id="caissiers-titre" class="text-lg font-bold">Par caissier</h3>
            @if (data.today.byCashier.length === 0) {
              <p class="mt-1 text-base text-ardoise">Aucune vente par caissier pour le moment.</p>
            } @else {
              <div class="relative mt-2 overflow-x-auto">
                <table class="w-full text-left text-base">
                  <caption class="sr-only">Ventes du jour par caissier</caption>
                  <thead class="border-b border-sauge text-sm text-ardoise">
                    <tr>
                      <th scope="col" class="py-1 pr-2">Caissier</th>
                      <th scope="col" class="px-2 py-1 text-right">Tickets</th>
                      <th scope="col" class="py-1 pl-2 text-right">Total</th>
                    </tr>
                  </thead>
                  <tbody class="divide-y divide-sauge">
                    @for (cashier of data.today.byCashier; track cashier.cashierId) {
                      <tr>
                        <th scope="row" class="py-2 pr-2 font-semibold">{{ nameOf(cashier.cashierId) }}</th>
                        <td class="amount px-2 py-2 text-right">{{ cashier.salesCount }}</td>
                        <td class="amount py-2 pl-2 text-right font-bold">{{ money(cashier.netTotalFcfa) }}</td>
                      </tr>
                    }
                  </tbody>
                </table>
              </div>
            }
          </section>
        }

        <section class="panel mb-3" aria-labelledby="classement-titre">
          <h3 id="classement-titre" class="text-lg font-bold">Meilleures ventes</h3>
          <div role="group" aria-label="Critère du classement" class="mt-2 flex flex-wrap gap-2">
            @for (option of criteria(); track option.value) {
              <button type="button" class="btn" [class.border-2]="criterion() === option.value" [class.border-feuille]="criterion() === option.value" [class.text-feuille]="criterion() === option.value" [attr.aria-pressed]="criterion() === option.value" (click)="selectCriterion(option.value)">{{ option.label }}</button>
            }
          </div>
          <div role="group" aria-label="Période du classement" class="mt-2 flex flex-wrap gap-2">
            @for (option of periods; track option.days) {
              <button type="button" class="btn" [class.border-2]="rankingDays() === option.days" [class.border-feuille]="rankingDays() === option.days" [class.text-feuille]="rankingDays() === option.days" [attr.aria-pressed]="rankingDays() === option.days" (click)="selectDays(option.days)">{{ option.label }}</button>
            }
          </div>
          @if (ranking().data; as items) {
            @if (items.length === 0) {
              <p class="mt-3 text-base text-ardoise">Aucune vente sur cette période.</p>
            } @else {
              <ol class="mt-3 divide-y divide-sauge">
                @for (item of items; track item.productId; let index = $index) {
                  <li class="py-2">
                    <div class="flex items-baseline gap-2">
                      <span class="amount w-6 shrink-0 text-ardoise">{{ index + 1 }}.</span>
                      <span class="min-w-0 flex-1 font-semibold">{{ item.name }}</span>
                      <span class="amount shrink-0 font-bold">{{ headline(item) }}</span>
                    </div>
                    <p class="amount pl-8 text-sm text-ardoise">{{ details(item) }}</p>
                  </li>
                }
              </ol>
              @if (criterion() === 'margin') {
                <p class="field-help">Marge estimée d’après le prix d’achat actuel de chaque produit : elle peut s’écarter de la réalité si ce prix a changé.</p>
              }
            }
          } @else if (ranking().error) {
            <p class="field-error" role="alert">{{ ranking().error }}</p>
          }
        </section>

        <section class="panel mb-3" aria-labelledby="caisses-titre">
          <h3 id="caisses-titre" class="text-lg font-bold">Caisses</h3>
          @if (sessions().data; as data) {
            <h4 class="mt-2 text-base font-semibold text-ardoise">Ouvertes en ce moment</h4>
            @if (data.open.length === 0) {
              <p class="text-base text-ardoise">Aucune caisse ouverte.</p>
            } @else {
              <ul class="divide-y divide-sauge">
                @for (session of data.open; track session.id) {
                  <li class="py-2">
                    <div class="flex items-baseline justify-between gap-2">
                      <span class="font-semibold">{{ nameOf(session.cashierId) }}</span>
                      <span class="amount text-sm">attendu en caisse : {{ money(session.expectedCashFcfa) }}</span>
                    </div>
                    <p class="text-sm text-ardoise">Ouverte depuis {{ since(session) }}</p>
                    @if (stale(session)) {
                      <p class="text-sm font-semibold text-avertissement">Ouverte depuis plus de 12 heures : à clôturer ?</p>
                    }
                  </li>
                }
              </ul>
            }

            <h4 class="mt-3 text-base font-semibold text-ardoise">Écarts de clôture du jour</h4>
            @if (data.variances.length === 0) {
              <p class="text-base text-ardoise">Aucun écart de caisse aujourd’hui.</p>
            } @else {
              <ul class="divide-y divide-sauge">
                @for (session of data.variances; track session.id) {
                  <li class="flex items-baseline justify-between gap-2 py-2">
                    <span class="font-semibold">{{ nameOf(session.cashierId) }}</span>
                    <span class="amount font-bold" [class.text-erreur]="(session.varianceFcfa ?? 0) < 0">{{ variance(session.varianceFcfa ?? 0) }}</span>
                  </li>
                }
              </ul>
              @if (activity().data; as report) {
                <p class="mt-1 text-sm text-ardoise">
                  Cumul du jour : {{ money(report.today.shortfallFcfa) }} de manques, {{ money(report.today.surplusFcfa) }} d’excédents.
                </p>
              }
            }
          } @else {
            <p class="field-error" role="alert">{{ sessions().error }}</p>
          }
        </section>

        @if (showStock()) {
          <section class="panel mb-3" aria-labelledby="stock-titre">
            <h3 id="stock-titre" class="text-lg font-bold">Stock</h3>
            @if (stock().data !== null) {
              @if (stock().data === 0) {
                <p class="text-base">Aucun produit sous son seuil d’alerte : le stock est correct.</p>
              } @else {
                <p class="text-base font-semibold text-avertissement">
                  {{ stock().data }} produit{{ stock().data! > 1 ? 's' : '' }} à réapprovisionner (au seuil d’alerte ou en dessous).
                </p>
              }
              <a class="btn mt-2" routerLink="/gestion/produits">Voir les produits</a>
            } @else {
              <p class="field-error" role="alert">{{ stock().error }}</p>
            }
          </section>
        }
      }
    </section>
  `,
})
export class DashboardPage implements OnInit, OnDestroy {
  private readonly api = inject(DashboardApi);
  private readonly products = inject(ProductsApi);
  private readonly team = inject(TeamApi);
  private readonly auth = inject(AuthService);
  protected readonly network = inject(NetworkService);

  protected readonly activity = signal<Block<ActivityBlock>>(empty());
  protected readonly sessions = signal<Block<SessionsBlock>>(empty());
  protected readonly stock = signal<Block<number>>(empty());
  protected readonly ranking = signal<Block<ProductRanking[]>>(empty());
  protected readonly criterion = signal<RankingCriterionName>('quantity');
  protected readonly rankingDays = signal(7);
  protected readonly names = signal<ReadonlyMap<string, string>>(new Map());
  protected readonly refreshing = signal(false);
  protected readonly firstLoad = signal(true);
  protected readonly announcement = signal('');
  private readonly now = signal(new Date());

  private timer: ReturnType<typeof setInterval> | null = null;
  private inFlight: Promise<void> | null = null;
  private readonly onVisibility = (): void => {
    if (document.visibilityState === 'visible' && this.isStale()) {
      void this.refresh();
    }
  };

  protected readonly showStock = computed(() => canManageStock(this.auth.user()?.role));
  /** La marge n'est proposée qu'à qui voit les prix d'achat : un comptable n'a que les deux autres classements. */
  protected readonly criteria = computed(() => (canSeeMargin(this.auth.user()?.role) ? CRITERIA : CRITERIA.filter((option) => option.value !== 'margin')));
  protected readonly periods = PERIODS;
  protected readonly trend = computed(() => {
    const data = this.activity().data;
    return data ? variation(data.today.netTotalFcfa, data.yesterday.netTotalFcfa) : { kind: 'none' as const, percent: 0 };
  });
  protected readonly trendText = computed(() => describeVariation(this.trend()));
  protected readonly basket = computed(() => {
    const data = this.activity().data;
    return data ? averageBasket(data.today.netTotalFcfa, data.today.salesCount) : null;
  });
  protected readonly shares = computed(() => paymentShares(this.activity().data?.today.byPaymentMethod ?? []));
  protected readonly dateLabel = computed(() =>
    this.now().toLocaleDateString('fr-FR', { weekday: 'long', day: 'numeric', month: 'long' }),
  );

  /** Heure de la dernière lecture réussie du bloc principal : on ne montre jamais des chiffres sans dire de quand ils sont. */
  protected readonly updatedLabel = computed(() => {
    const at = this.activity().at;
    return at ? `Mis à jour à ${time(at)}` : 'Pas encore de lecture';
  });

  protected readonly staleWarning = computed(() => {
    const failing = [
      this.activity().error && this.activity().data ? 'chiffres du jour' : '',
      this.sessions().error && this.sessions().data ? 'caisses' : '',
      this.stock().error && this.stock().data !== null ? 'stock' : '',
      this.ranking().error && this.ranking().data ? 'meilleures ventes' : '',
    ].filter(Boolean);
    const at = this.activity().at;
    return failing.length > 0
      ? `L’actualisation a échoué (${failing.join(', ')}) : ces données restent celles de la dernière lecture${at ? `, à ${time(at)}` : ''}.`
      : '';
  });

  protected money = formatFcfa;
  protected payment = paymentLabel;
  protected variance = describeVariance;

  async ngOnInit(): Promise<void> {
    document.addEventListener('visibilitychange', this.onVisibility);
    this.timer = setInterval(() => {
      if (document.visibilityState === 'visible' && this.network.online()) {
        void this.refresh();
      }
    }, REFRESH_EVERY_MS);
    await this.refresh();
  }

  ngOnDestroy(): void {
    document.removeEventListener('visibilitychange', this.onVisibility);
    if (this.timer !== null) {
      clearInterval(this.timer);
      this.timer = null;
    }
  }

  /** Une seule lecture à la fois : un clic pendant une lecture automatique (ou l'inverse) rejoint celle en cours (SC-D7). */
  refresh(): Promise<void> {
    this.inFlight ??= this.load().finally(() => {
      this.inFlight = null;
    });
    return this.inFlight;
  }

  protected selectCriterion(value: RankingCriterionName): void {
    this.criterion.set(value);
    void this.refreshRanking();
  }

  protected selectDays(days: number): void {
    this.rankingDays.set(days);
    void this.refreshRanking();
  }

  /** Premier chiffre de la ligne : celui du critère choisi. */
  protected headline(item: ProductRanking): string {
    switch (this.criterion()) {
      case 'revenue':
        return formatFcfa(item.revenueFcfa);
      case 'margin':
        return item.marginFcfa === null ? 'marge inconnue' : formatFcfa(item.marginFcfa);
      default:
        return `${item.quantity} vendu${item.quantity > 1 ? 's' : ''}`;
    }
  }

  /** Les deux autres chiffres, pour comparer sans changer de classement. */
  protected details(item: ProductRanking): string {
    switch (this.criterion()) {
      case 'revenue':
        return `${item.quantity} vendu${item.quantity > 1 ? 's' : ''}`;
      case 'margin':
        return `${item.quantity} vendu${item.quantity > 1 ? 's' : ''} · ${formatFcfa(item.revenueFcfa)} de chiffre d’affaires`;
      default:
        return `${formatFcfa(item.revenueFcfa)} de chiffre d’affaires`;
    }
  }

  /** Relit le seul classement (changement de critère ou de période) : le reste du tableau de bord n'est pas relu. */
  private async refreshRanking(): Promise<void> {
    const now = new Date();
    const [result] = await Promise.allSettled([this.api.topProducts(lastDaysRange(now, this.rankingDays()), this.criterion(), RANKING_SIZE)]);
    this.apply(this.ranking, result, now, (list) => list);
  }

  protected nameOf(userId: string): string {
    return this.names().get(userId) ?? `Caissier ${userId.slice(0, 8)}`;
  }

  protected since(session: SessionSummary): string {
    const hours = hoursOpen(session.openedAt, this.now());
    const opened = new Date(session.openedAt);
    const clock = time(opened);
    return hours < 1 ? `${clock} (il y a ${Math.max(Math.round(hours * 60), 1)} min)` : `${clock} (il y a ${Math.floor(hours)} h)`;
  }

  protected stale(session: SessionSummary): boolean {
    return isStaleSession(session.openedAt, this.now());
  }

  private isStale(): boolean {
    const at = this.activity().at;
    return at === null || new Date().getTime() - at.getTime() >= REFRESH_EVERY_MS;
  }

  private async load(): Promise<void> {
    this.refreshing.set(true);
    const now = new Date();
    this.now.set(now);
    try {
      const today = todayRange(now);
      const yesterday = yesterdaySameTimeRange(now);
      // La clôture de ce matin concerne parfois une caisse ouverte hier soir : on lit depuis la veille et on filtre ensuite.
      const openedFrom = startOfLocalDay(new Date(now.getFullYear(), now.getMonth(), now.getDate() - 1)).toISOString();
      const startOfToday = startOfLocalDay(now).getTime();

      const [activity, sessions, stock, directory, ranking] = await Promise.allSettled([
        Promise.all([this.api.activity(today), this.api.activity(yesterday)]),
        Promise.all([this.api.openSessions(), this.api.closedWithVariance(openedFrom, new Date(now.getTime() + 60_000).toISOString())]),
        this.showStock() ? this.products.lowStock() : Promise.resolve(null),
        this.team.directory(),
        this.api.topProducts(lastDaysRange(now, this.rankingDays()), this.criterion(), RANKING_SIZE),
      ]);

      this.apply(this.activity, activity, now, ([todayReport, yesterdayReport]) => ({ today: todayReport, yesterday: yesterdayReport }));
      this.apply(this.sessions, sessions, now, ([open, closed]) => ({
        open,
        variances: closed.filter((session) => session.closedAt !== null && Date.parse(session.closedAt) >= startOfToday),
      }));
      if (stock.status === 'fulfilled' && stock.value === null) {
        this.stock.set(empty());
      } else {
        this.apply(this.stock, stock as PromiseSettledResult<{ length: number }>, now, (list) => list.length);
      }
      this.apply(this.ranking, ranking, now, (list) => list);
      if (directory.status === 'fulfilled') {
        this.names.set(new Map(directory.value.map((entry) => [entry.id, entry.displayName])));
      }

      if (this.firstLoad()) {
        this.firstLoad.set(false);
      }
      this.announcement.set(`Données actualisées à ${time(now)}.`);
    } finally {
      this.refreshing.set(false);
    }
  }

  /** Une lecture réussie remplace le bloc ; un échec garde les derniers chiffres et note l'erreur. */
  private apply<T, R>(target: ReturnType<typeof signal<Block<T>>>, result: PromiseSettledResult<R>, at: Date, build: (value: R) => T): void {
    if (result.status === 'fulfilled') {
      target.set({ data: build(result.value), at, error: '' });
    } else {
      target.update((previous) => ({ ...previous, error: describeError(result.reason) }));
    }
  }
}

function time(moment: Date): string {
  return moment.toLocaleTimeString('fr-FR', { hour: '2-digit', minute: '2-digit' });
}
