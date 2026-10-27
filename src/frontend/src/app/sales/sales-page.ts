import { Component, computed, inject, OnInit, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { formatFcfa } from '../core/money';
import { TeamApi } from '../management/team-api';
import { AuthService } from '../services/auth.service';
import { NetworkService } from '../services/network.service';
import { ApiError } from '../sync/sync-api';
import { SaleSummary, SalesApi } from './sales-api';

const DAYS = 7;

/** Retrouver un ticket pour annuler une vente ou reprendre un article (docs/specs/annulations-remises-retours ARR-09). En ligne seulement. */
@Component({
  selector: 'app-sales-page',
  imports: [RouterLink],
  template: `
    <section class="mx-auto max-w-2xl" aria-labelledby="ventes-titre">
      <h2 id="ventes-titre" class="mb-1 text-xl font-bold">Ventes</h2>
      <p class="mb-4 text-base text-ardoise">Les ventes des {{ days }} derniers jours{{ isManager() ? '' : ' que vous avez faites' }}.</p>

      @if (!network.online()) {
        <p class="panel font-semibold text-avertissement" role="status">Cet écran a besoin du réseau : la caisse continue de vendre, mais les annulations et les retours attendent le retour du réseau.</p>
      } @else {
        <label class="field-label" for="recherche-ticket">Numéro de ticket</label>
        <input id="recherche-ticket" class="field mb-4" type="search" inputmode="numeric" autocomplete="off" [value]="term()" (input)="term.set($any($event.target).value)" />

        @if (loading()) {
          <p class="panel text-ardoise" role="status">Chargement des ventes…</p>
        } @else if (error()) {
          <div class="panel" role="alert">
            <p class="font-semibold text-erreur">{{ error() }}</p>
            <button type="button" class="btn mt-3" (click)="load()">Réessayer</button>
          </div>
        } @else if (shown().length === 0) {
          <p class="panel text-ardoise">{{ term() ? 'Aucun ticket ne porte ce numéro.' : 'Aucune vente sur cette période.' }}</p>
        } @else {
          <ul class="flex flex-col gap-2">
            @for (item of shown(); track item.id) {
              <li>
                <a class="panel flex flex-wrap items-center gap-3" [routerLink]="['/ventes', item.id]" [attr.aria-label]="'Ouvrir le ticket ' + item.ticket">
                  <span class="min-w-0 flex-1">
                    <span class="block text-lg font-semibold">Ticket n° {{ item.ticket }}</span>
                    <span class="block text-sm text-ardoise">{{ when(item.recordedAt) }}@if (isManager()) { · {{ cashier(item.cashierId) }} }</span>
                    <span class="block text-sm font-semibold" [class.text-erreur]="item.status === 'Cancelled'" [class.text-avertissement]="item.returnedFcfa > 0 && item.status !== 'Cancelled'">{{ state(item) }}</span>
                  </span>
                  <span class="amount text-lg font-bold">{{ format(item.netTotalFcfa) }}</span>
                </a>
              </li>
            }
          </ul>
        }
      }
    </section>
  `,
})
export class SalesPage implements OnInit {
  private readonly api = inject(SalesApi);
  private readonly auth = inject(AuthService);
  private readonly team = inject(TeamApi);
  protected readonly network = inject(NetworkService);

  protected readonly days = DAYS;
  protected readonly sales = signal<SaleSummary[]>([]);
  protected readonly names = signal<ReadonlyMap<string, string>>(new Map());
  protected readonly loading = signal(true);
  protected readonly error = signal('');
  protected readonly term = signal('');
  protected readonly isManager = computed(() => ['Owner', 'Manager'].includes(this.auth.user()?.role ?? ''));
  protected readonly shown = computed(() => {
    const wanted = this.term().replace(/\D/g, '');
    return this.sales().filter((item) => wanted === '' || String(item.ticket) === wanted);
  });
  protected format = formatFcfa;

  async ngOnInit(): Promise<void> {
    if (this.network.online()) {
      await this.load();
    }
  }

  protected async load(): Promise<void> {
    this.loading.set(true);
    this.error.set('');
    try {
      const earliest = Date.now() - DAYS * 86_400_000;
      this.sales.set((await this.api.list()).filter((item) => Date.parse(item.recordedAt) >= earliest));
      if (this.isManager()) {
        this.names.set(new Map((await this.team.directory().catch(() => [])).map((entry) => [entry.id, entry.displayName])));
      }
    } catch (error) {
      this.sales.set([]);
      this.error.set(error instanceof ApiError && error.status === 0 ? 'Le serveur ne répond pas. Vérifiez le réseau puis réessayez.' : 'Les ventes n’ont pas pu être chargées. Réessayez.');
    } finally {
      this.loading.set(false);
    }
  }

  protected cashier(id: string): string {
    return this.names().get(id) ?? 'Un collègue';
  }

  protected when(iso: string): string {
    return new Date(iso).toLocaleString('fr-FR', { dateStyle: 'short', timeStyle: 'short' });
  }

  protected state(item: SaleSummary): string {
    if (item.status === 'Cancelled') {
      return 'Annulée';
    }
    return item.returnedFcfa > 0 ? `Retour partiel : ${formatFcfa(item.returnedFcfa)} rendus` : 'Validée';
  }
}
