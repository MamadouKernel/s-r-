import { Component, effect, inject, signal } from '@angular/core';
import { formatFcfa } from '../core/money';
import { Operation } from '../data/journal';
import { JournalService } from '../services/journal.service';
import { SyncService } from '../services/sync.service';

const MOVEMENT_LABEL = { Expense: 'Dépense', Drop: 'Versement au coffre', Topup: 'Apport de monnaie' } as const;
const STATUS_LABEL = { pending: 'En attente', synced: 'Synchronisée', rejected: 'Refusée' } as const;

/** S6 : ce qui est en attente, envoyé ou refusé. Un rejet reste visible avec son motif, jamais supprimé (REQ-06). */
@Component({
  selector: 'app-queue-page',
  template: `
    <section>
      <div class="mb-4 flex flex-wrap items-center gap-3">
        <h2 class="text-xl font-bold">File d’envoi</h2>
        <button type="button" class="btn-primary ml-auto" (click)="sendNow()" [disabled]="sync.counts().pending === 0 || sync.state() === 'syncing'">
          {{ sync.state() === 'syncing' ? 'Envoi en cours…' : 'Envoyer maintenant' }}
        </button>
      </div>

      @if (operations().length === 0) {
        <p class="panel text-ardoise">Aucune opération pour le moment. Les ventes apparaîtront ici dès qu’elles seront enregistrées.</p>
      } @else {
        <ul class="divide-y divide-sauge rounded border border-sauge bg-surface">
          @for (operation of operations(); track operation.seq) {
            <li class="px-4 py-3">
              <div class="flex flex-wrap items-baseline gap-x-3 gap-y-1">
                <span class="amount text-sm text-ardoise">N° {{ operation.seq }}</span>
                <span class="font-semibold">{{ title(operation) }}</span>
                <span
                  class="ml-auto text-sm font-semibold"
                  [class.text-avertissement]="operation.status === 'pending'"
                  [class.text-succes]="operation.status === 'synced'"
                  [class.text-erreur]="operation.status === 'rejected'"
                  >{{ label(operation.status) }}</span
                >
              </div>
              @if (operation.status === 'rejected') {
                <p class="mt-1 text-base text-erreur" role="note">{{ operation.message }}</p>
              }
            </li>
          }
        </ul>
      }
    </section>
  `,
})
export class QueuePage {
  private readonly journalService = inject(JournalService);
  protected readonly sync = inject(SyncService);
  protected readonly operations = signal<Operation[]>([]);

  constructor() {
    // La liste suit les compteurs de synchronisation : elle se met à jour dès qu'une opération change d'état.
    effect(() => {
      this.sync.counts();
      void this.reload();
    });
  }

  protected async sendNow(): Promise<void> {
    await this.sync.requestFlush();
    await this.reload();
  }

  protected label(status: Operation['status']): string {
    return STATUS_LABEL[status];
  }

  protected title(operation: Operation): string {
    switch (operation.kind) {
      case 'openSession':
        return `Ouverture de caisse · fond ${formatFcfa(operation.openingFloatFcfa)}`;
      case 'sale':
        return `Vente · ${formatFcfa(operation.netTotalFcfa)}`;
      case 'cashMovement':
        return `${MOVEMENT_LABEL[operation.movementKind]} · ${formatFcfa(operation.amountFcfa)}${operation.reason ? ` · ${operation.reason}` : ''}`;
      case 'closeSession':
        // Clôture à l'aveugle : jamais l'écart ni l'attendu à l'écran du caissier (docs/decisions/0021).
        return `Clôture · compté ${formatFcfa(operation.countedCashFcfa)}`;
    }
  }

  private async reload(): Promise<void> {
    const journal = await this.journalService.get();
    this.operations.set((await journal.operations()).reverse());
  }
}
