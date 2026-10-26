import { computed, inject, Injectable, Injector, signal } from '@angular/core';
import { CustomerService } from '../customers/customer.service';
import { OperationStatus } from '../data/journal';
import { nextDelayMs } from '../sync/backoff';
import { FlushOutcome, SyncEngine } from '../sync/sync-engine';
import { ApiService } from './api.service';
import { AuthService } from './auth.service';
import { JournalService } from './journal.service';
import { NetworkService } from './network.service';

export type SyncState = 'idle' | 'syncing' | 'waiting' | 'auth' | 'blocked';

/**
 * Pilote le moteur de synchronisation : au démarrage, au retour du réseau, après chaque écriture locale et en reprise avec un
 * délai progressif après un échec (PWA-07). Expose l'état et les compteurs pour le bandeau réseau.
 */
@Injectable({ providedIn: 'root' })
export class SyncService {
  private readonly journalService = inject(JournalService);
  private readonly api = inject(ApiService);
  private readonly network = inject(NetworkService);
  private readonly auth = inject(AuthService);
  private readonly injector = inject(Injector);

  private readonly stateSignal = signal<SyncState>('idle');
  private readonly countsSignal = signal<Record<OperationStatus, number>>({ pending: 0, synced: 0, rejected: 0 });
  private engine: SyncEngine | null = null;
  private failedAttempts = 0;
  private timer: ReturnType<typeof setTimeout> | null = null;

  readonly state = this.stateSignal.asReadonly();
  readonly counts = this.countsSignal.asReadonly();
  /** Libellé du bandeau : « En ligne », « Hors ligne » ou « N vente(s) en attente… » (DESIGN.md §4). */
  readonly banner = computed(() => {
    const { pending, rejected } = this.countsSignal();
    if (rejected > 0) {
      return { tone: 'error' as const, text: `${rejected} opération${rejected > 1 ? 's' : ''} refusée${rejected > 1 ? 's' : ''} par le serveur` };
    }
    if (!this.network.online()) {
      return { tone: 'warning' as const, text: pending > 0 ? `Hors ligne · ${pending} en attente de synchronisation` : 'Hors ligne' };
    }
    if (this.stateSignal() === 'auth' && pending > 0) {
      return { tone: 'warning' as const, text: `${pending} en attente · reconnectez-vous pour envoyer` };
    }
    if (pending > 0) {
      return { tone: 'info' as const, text: `${pending} en attente de synchronisation` };
    }
    return { tone: 'ok' as const, text: 'En ligne · tout est synchronisé' };
  });

  /** À appeler une fois : écoute le retour du réseau et envoie ce qui attend. */
  start(): void {
    window.addEventListener('online', () => void this.requestFlush());
    void this.refreshCounts().then(() => this.requestFlush());
  }

  async refreshCounts(): Promise<void> {
    this.countsSignal.set(await (await this.journalService.get()).counts());
  }

  /** Demande un envoi dès que possible : sans effet hors ligne, sans jeton valide ou si un envoi est déjà en cours. */
  async requestFlush(): Promise<FlushOutcome | null> {
    this.clearTimer();
    this.auth.tick();
    await this.refreshCounts();
    if (this.network.online() && this.auth.signedIn()) {
      await this.sendOfflineLogins();
    }
    if (this.countsSignal().pending === 0) {
      this.stateSignal.set('idle');
      return null;
    }
    if (!this.network.online()) {
      this.stateSignal.set('waiting');
      return null;
    }
    if (!this.auth.signedIn()) {
      this.stateSignal.set('auth');
      return null;
    }

    this.stateSignal.set('syncing');
    this.engine ??= new SyncEngine(await this.journalService.get(), this.api);
    const outcome = await this.engine.flush();
    await this.refreshCounts();
    this.apply(outcome);
    return outcome;
  }

  /** Les entrées sans réseau terminées partent au journal du serveur ; un échec les garde pour le prochain envoi (le serveur ignore un rejeu). */
  private async sendOfflineLogins(): Promise<void> {
    const journal = await this.journalService.get();
    const ended = (await journal.getOfflineLogins()).filter((event) => event.endedAt !== null);
    if (ended.length === 0) {
      return;
    }
    try {
      await this.api.sendOfflineLogins(ended);
      const sent = new Set(ended.map((event) => event.eventId));
      await journal.saveOfflineLogins((await journal.getOfflineLogins()).filter((event) => !sent.has(event.eventId)));
    } catch {
      // Réseau ou serveur indisponible : rien n'est perdu, on réessaiera au prochain envoi.
    }
  }

  private apply(outcome: FlushOutcome): void {
    switch (outcome.stopped) {
      case 'none':
        this.failedAttempts = 0;
        this.stateSignal.set('idle');
        this.refreshCustomers();
        break;
      case 'network':
        this.stateSignal.set('waiting');
        this.timer = setTimeout(() => void this.requestFlush(), nextDelayMs(this.failedAttempts++));
        break;
      case 'auth':
        this.stateSignal.set('auth');
        break;
      case 'blocked':
        this.stateSignal.set('blocked');
        break;
    }
  }

  /**
   * Les ventes à crédit viennent d'être envoyées : on recale les dettes locales sur celles du serveur. Sans effet visible si cela échoue : la
   * caisse garde sa copie, qui tient compte des ventes de cet appareil.
   */
  private refreshCustomers(): void {
    try {
      void this.injector.get(CustomerService).refresh();
    } catch {
      // Pas de client HTTP ou pas de réseau : la copie locale reste utilisable.
    }
  }

  private clearTimer(): void {
    if (this.timer !== null) {
      clearTimeout(this.timer);
      this.timer = null;
    }
  }
}
