import { Component, inject, OnInit, signal } from '@angular/core';
import { lastDaysRange } from '../dashboard/period';
import { describeError } from '../management/errors';
import { TeamApi } from '../management/team-api';
import { NetworkService } from '../services/network.service';
import { AuditApi, AuditEntry } from './audit-api';
import { actionLabel, CATEGORIES } from './audit-labels';

const PAGE_SIZE = 50;

const PERIODS: readonly { days: number; label: string }[] = [
  { days: 1, label: 'Aujourd’hui' },
  { days: 7, label: '7 jours' },
  { days: 30, label: '30 jours' },
];

/** MV-05 : le journal des actions sensibles, en lecture seule, du plus récent au plus ancien. En ligne seulement. */
@Component({
  selector: 'app-journal-page',
  template: `
    <section aria-labelledby="journal-titre">
      <h2 id="journal-titre" class="mb-1 text-xl font-bold">Journal des actions sensibles</h2>
      <p class="mb-3 text-base text-ardoise">Qui a fait quoi, et quand : remises, annulations, changements de fiche produit, pertes de stock, comptes désactivés.</p>

      @if (!network.online()) {
        <p class="panel mb-3 font-semibold text-avertissement" role="note">Vous êtes hors ligne : cet écran a besoin du réseau.</p>
      }

      <div role="group" aria-label="Catégorie d’actions" class="mb-2 flex flex-wrap gap-2">
        @for (option of categories; track option.label) {
          <button type="button" class="btn" [class.border-2]="category() === option.value" [class.border-feuille]="category() === option.value" [class.text-feuille]="category() === option.value" [attr.aria-pressed]="category() === option.value" (click)="selectCategory(option.value)">{{ option.label }}</button>
        }
      </div>
      <div role="group" aria-label="Période" class="mb-4 flex flex-wrap gap-2">
        @for (option of periods; track option.days) {
          <button type="button" class="btn" [class.border-2]="days() === option.days" [class.border-feuille]="days() === option.days" [class.text-feuille]="days() === option.days" [attr.aria-pressed]="days() === option.days" (click)="selectDays(option.days)">{{ option.label }}</button>
        }
      </div>

      @if (loading() && entries().length === 0) {
        <p class="panel text-ardoise" role="status">Chargement du journal…</p>
      } @else if (error() && entries().length === 0) {
        <div class="panel" role="alert">
          <p class="font-semibold text-erreur">{{ error() }}</p>
          <button type="button" class="btn mt-3" (click)="reload()">Réessayer</button>
        </div>
      } @else if (entries().length === 0) {
        <p class="panel text-ardoise">Aucune action sensible sur cette période.</p>
      } @else {
        <div class="relative overflow-x-auto rounded border border-sauge bg-surface">
          <table class="w-full min-w-[40rem] text-left text-base">
            <caption class="sr-only">Journal des actions sensibles, du plus récent au plus ancien</caption>
            <thead class="border-b border-sauge text-sm text-ardoise">
              <tr>
                <th scope="col" class="px-3 py-2">Date et heure</th>
                <th scope="col" class="px-3 py-2">Action</th>
                <th scope="col" class="px-3 py-2">Auteur</th>
                <th scope="col" class="px-3 py-2">Détail</th>
              </tr>
            </thead>
            <tbody class="divide-y divide-sauge">
              @for (entry of entries(); track entry.id) {
                <tr>
                  <td class="amount whitespace-nowrap px-3 py-2 text-sm">{{ date(entry.at) }}</td>
                  <th scope="row" class="px-3 py-2 font-semibold" [class.text-erreur]="refused(entry)">{{ label(entry.action) }}</th>
                  <td class="px-3 py-2">{{ nameOf(entry.userId) }}</td>
                  <td class="px-3 py-2 text-sm">{{ entry.detail || '—' }}</td>
                </tr>
              }
            </tbody>
          </table>
        </div>
        @if (error()) {
          <p class="field-error" role="alert">{{ error() }}</p>
        }
        <div class="mt-3 flex flex-wrap items-center gap-3">
          @if (hasMore()) {
            <button type="button" class="btn-primary" (click)="loadMore()" [disabled]="loading()">{{ loading() ? 'Chargement…' : 'Charger plus' }}</button>
          } @else {
            <p class="text-sm text-ardoise">Fin du journal pour cette période.</p>
          }
          <span class="text-sm text-ardoise" role="status" aria-live="polite">{{ entries().length }} action{{ entries().length > 1 ? 's' : '' }} affichée{{ entries().length > 1 ? 's' : '' }}</span>
        </div>
      }
    </section>
  `,
})
export class JournalPage implements OnInit {
  private readonly api = inject(AuditApi);
  private readonly team = inject(TeamApi);
  protected readonly network = inject(NetworkService);

  protected readonly entries = signal<AuditEntry[]>([]);
  protected readonly hasMore = signal(false);
  protected readonly loading = signal(false);
  protected readonly error = signal('');
  protected readonly category = signal<string | null>(null);
  protected readonly days = signal(7);
  protected readonly names = signal<ReadonlyMap<string, string>>(new Map());

  protected readonly categories = CATEGORIES;
  protected readonly periods = PERIODS;
  protected label = actionLabel;

  /** Chaque lecture a un numéro : la réponse d'un filtre abandonné ne doit jamais écraser celle du filtre courant. */
  private generation = 0;

  async ngOnInit(): Promise<void> {
    void this.loadNames();
    await this.reload();
  }

  protected selectCategory(value: string | null): void {
    this.category.set(value);
    void this.reload();
  }

  protected selectDays(days: number): void {
    this.days.set(days);
    void this.reload();
  }

  protected async reload(): Promise<void> {
    this.entries.set([]);
    this.hasMore.set(false);
    await this.load(0);
  }

  protected async loadMore(): Promise<void> {
    await this.load(this.entries().length);
  }

  protected date(iso: string): string {
    return new Date(iso).toLocaleString('fr-FR', { dateStyle: 'short', timeStyle: 'short' });
  }

  /** Les refus (remise ou annulation refusée) sont signalés en toutes lettres par leur libellé, et soulignés en couleur en plus. */
  protected refused(entry: AuditEntry): boolean {
    return entry.action.endsWith('.refused');
  }

  protected nameOf(userId: string): string {
    return this.names().get(userId) ?? `Utilisateur ${userId.slice(0, 8)}`;
  }

  private async load(offset: number): Promise<void> {
    const generation = ++this.generation;
    this.loading.set(true);
    this.error.set('');
    try {
      const page = await this.api.list({ range: lastDaysRange(new Date(), this.days()), category: this.category(), limit: PAGE_SIZE, offset });
      if (generation !== this.generation) {
        return;
      }
      this.entries.update((current) => (offset === 0 ? page.entries : [...current, ...page.entries]));
      this.hasMore.set(page.hasMore);
    } catch (error) {
      if (generation === this.generation) {
        this.error.set(describeError(error));
      }
    } finally {
      if (generation === this.generation) {
        this.loading.set(false);
      }
    }
  }

  private async loadNames(): Promise<void> {
    try {
      this.names.set(new Map((await this.team.directory()).map((entry) => [entry.id, entry.displayName])));
    } catch {
      this.names.set(new Map()); // sans annuaire, l'identifiant court sert de repli
    }
  }
}
