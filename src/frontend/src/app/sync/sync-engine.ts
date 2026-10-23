import { CloseSessionOperation, Journal, OpenSessionOperation, Operation, SaleOperation } from '../data/journal';
import { ApiError, SyncApi } from './sync-api';

/** Un lot contient au plus 200 ventes (limite du serveur). */
export const MAX_BATCH = 200;

export type StopReason = 'none' | 'network' | 'auth' | 'blocked';

export interface FlushOutcome {
  /** Opérations acquittées par le serveur pendant cet envoi. */
  sent: number;
  rejected: number;
  stopped: StopReason;
}

/**
 * Envoie la file du journal au serveur dans l'ordre de la caisse (PWA-07, REQ-06). Une vente rejetée reste visible avec son motif et
 * n'empêche pas les suivantes ; un échec réseau arrête l'envoi sans rien perdre ; tout est idempotent par UUID d'origine.
 */
export class SyncEngine {
  private running: Promise<FlushOutcome> | null = null;

  constructor(
    private readonly journal: Journal,
    private readonly api: SyncApi,
  ) {}

  /** Deux appels simultanés partagent le même envoi. */
  flush(): Promise<FlushOutcome> {
    this.running ??= this.run().finally(() => {
      this.running = null;
    });
    return this.running;
  }

  private async run(): Promise<FlushOutcome> {
    const outcome: FlushOutcome = { sent: 0, rejected: 0, stopped: 'none' };
    let pending = await this.journal.pendingOperations();

    while (pending.length > 0 && outcome.stopped === 'none') {
      const head = pending[0];
      try {
        if (head.kind === 'sale') {
          const batch = takeSales(pending);
          await this.sendSales(batch, outcome);
          pending = pending.slice(batch.length);
        } else if (head.kind === 'openSession') {
          await this.sendOpening(head, outcome);
          pending = pending.slice(1);
        } else {
          await this.sendClosing(head, outcome);
          pending = pending.slice(1);
        }
      } catch (error) {
        outcome.stopped = stopReason(error);
      }
    }
    return outcome;
  }

  private async sendOpening(operation: OpenSessionOperation, outcome: FlushOutcome): Promise<void> {
    try {
      await retryOnConflict(() => this.api.openSession(operation));
      await this.journal.markSynced(operation.seq);
      outcome.sent++;
    } catch (error) {
      if (isDefinitive(error)) {
        await this.journal.markRejected(operation.seq, error.code, error.message);
        outcome.rejected++;
        // Les ventes dépendent de la session : inutile de les envoyer tant que le problème n'est pas réglé.
        throw new BlockedError();
      }
      throw error;
    }
  }

  private async sendSales(batch: SaleOperation[], outcome: FlushOutcome): Promise<void> {
    const statuses = await retryOnConflict(() => this.api.syncSales(batch));
    const bySaleId = new Map(statuses.map((status) => [status.saleId, status]));
    for (const sale of batch) {
      const status = bySaleId.get(sale.saleId);
      if (!status) {
        continue; // pas d'accusé : la vente reste en attente et sera renvoyée
      }
      if (status.outcome === 'Rejected') {
        await this.journal.markRejected(sale.seq, status.errorCode ?? 'rejected', status.message ?? 'Vente refusée par le serveur.');
        outcome.rejected++;
      } else {
        await this.journal.markSynced(sale.seq);
        outcome.sent++;
      }
    }
  }

  private async sendClosing(operation: CloseSessionOperation, outcome: FlushOutcome): Promise<void> {
    try {
      const result = await retryOnConflict(() => this.api.closeSession(operation));
      await this.journal.markSynced(operation.seq, { result });
      outcome.sent++;
    } catch (error) {
      if (isDefinitive(error)) {
        await this.journal.markRejected(operation.seq, error.code, error.message);
        outcome.rejected++;
        return;
      }
      throw error;
    }
  }
}

class BlockedError extends Error {}

function takeSales(pending: Operation[]): SaleOperation[] {
  const batch: SaleOperation[] = [];
  for (const operation of pending) {
    if (operation.kind !== 'sale' || batch.length === MAX_BATCH) {
      break;
    }
    batch.push(operation);
  }
  return batch;
}

/** Refus définitif du serveur (erreur du client, pas du réseau ni de l'authentification) : à montrer, pas à rejouer. */
function isDefinitive(error: unknown): error is ApiError {
  return error instanceof ApiError && error.status >= 400 && error.status < 500 && error.status !== 401 && error.status !== 429;
}

function stopReason(error: unknown): StopReason {
  if (error instanceof BlockedError) {
    return 'blocked';
  }
  if (error instanceof ApiError && error.status === 401) {
    return 'auth';
  }
  return 'network';
}

/** Deux écritures concurrentes de la même clé : le serveur demande de renvoyer la requête, déjà idempotente. */
async function retryOnConflict<T>(call: () => Promise<T>): Promise<T> {
  try {
    return await call();
  } catch (error) {
    if (error instanceof ApiError && error.status === 409 && error.code === 'concurrent_conflict') {
      return call();
    }
    throw error;
  }
}
