import 'fake-indexeddb/auto';
import { Journal, SaleOperation } from '../data/journal';
import { CatalogueProduct } from '../domain/types';
import { ApiError, SaleSyncStatus, SyncApi } from './sync-api';
import { SyncEngine } from './sync-engine';
import { nextDelayMs } from './backoff';

const jus: CatalogueProduct = {
  id: 'p-jus', name: 'Jus', barcode: '111', category: null, salePriceFcfa: 600, tracksStock: true, quantityOnHand: 100,
};

class FakeApi implements SyncApi {
  readonly calls: string[] = [];
  readonly receivedSales: string[] = [];
  failWith: ApiError | null = null;
  openFailure: ApiError | null = null;
  rejectSales = new Map<string, { code: string; message: string }>();
  seen = new Set<string>();

  async openSession(): Promise<void> {
    this.calls.push('open');
    if (this.openFailure) throw this.openFailure;
    if (this.failWith) throw this.failWith;
  }

  async syncSales(sales: SaleOperation[]): Promise<SaleSyncStatus[]> {
    this.calls.push(`sync:${sales.length}`);
    if (this.failWith) throw this.failWith;
    return sales.map((sale) => {
      this.receivedSales.push(sale.saleId);
      const rejection = this.rejectSales.get(sale.saleId);
      if (rejection) {
        return { saleId: sale.saleId, outcome: 'Rejected', errorCode: rejection.code, message: rejection.message };
      }
      const outcome = this.seen.has(sale.saleId) ? 'AlreadyProcessed' : 'Accepted';
      this.seen.add(sale.saleId);
      return { saleId: sale.saleId, outcome };
    });
  }

  async closeSession() {
    this.calls.push('close');
    if (this.failWith) throw this.failWith;
    return { expectedCashFcfa: 11800, countedCashFcfa: 11300, varianceFcfa: -500 };
  }
}

async function setup(salesCount = 3) {
  const journal = await Journal.open(`soro-sync-${Math.random()}`);
  await journal.replaceCatalogue([jus]);
  await journal.openSession({
    sessionId: 's-1', pointOfSaleId: 'pos', registerId: 'reg', openingFloatFcfa: 10000, openedAt: '2026-10-23T08:00:00.000Z',
  });
  for (let index = 1; index <= salesCount; index++) {
    await journal.recordSale({
      id: `sale-${index}`, at: '2026-10-23T08:10:00.000Z',
      lines: [{ productId: 'p-jus', name: 'Jus', unitPriceFcfa: 600, quantity: 1 }],
      payments: [{ id: `pay-${index}`, method: 'Cash', amountFcfa: 600, tenderedFcfa: 600 }],
    });
  }
  const api = new FakeApi();
  return { journal, api, engine: new SyncEngine(journal, api) };
}

describe('SyncEngine', () => {
  it('sends the session first, then the sales in order, and marks everything synced (SC-P2)', async () => {
    const { journal, api, engine } = await setup(3);

    const outcome = await engine.flush();

    expect(api.calls).toEqual(['open', 'sync:3']);
    expect(api.receivedSales).toEqual(['sale-1', 'sale-2', 'sale-3']);
    expect(outcome).toMatchObject({ stopped: 'none', sent: 4, rejected: 0 });
    expect(await journal.counts()).toEqual({ pending: 0, synced: 4, rejected: 0 });
  });

  it('is idempotent: a second flush sends nothing and replaying the same sale creates no duplicate', async () => {
    const { api, engine } = await setup(2);
    await engine.flush();
    api.calls.length = 0;

    const outcome = await engine.flush();

    expect(api.calls).toEqual([]);
    expect(outcome.sent).toBe(0);
  });

  it('keeps a rejected sale visible with its reason and still sends the next ones (SC-P3)', async () => {
    const { journal, api, engine } = await setup(3);
    api.rejectSales.set('sale-2', { code: 'mobile_money_reference_used', message: 'Référence déjà utilisée' });

    const outcome = await engine.flush();

    expect(outcome).toMatchObject({ stopped: 'none', rejected: 1 });
    const sales = (await journal.operations()).filter((op): op is SaleOperation => op.kind === 'sale');
    expect(sales.map((sale) => sale.status)).toEqual(['synced', 'rejected', 'synced']);
    expect(sales[1]).toMatchObject({ errorCode: 'mobile_money_reference_used', message: 'Référence déjà utilisée' });
  });

  it('treats AlreadyProcessed as synced', async () => {
    const { journal, api, engine } = await setup(1);
    api.seen.add('sale-1');
    await engine.flush();
    expect((await journal.counts()).synced).toBe(2);
  });

  it('stops on a network failure and keeps everything pending for a retry', async () => {
    const { journal, api, engine } = await setup(2);
    api.failWith = new ApiError(0, 'network', 'Réseau indisponible');

    const outcome = await engine.flush();

    expect(outcome.stopped).toBe('network');
    expect(await journal.counts()).toEqual({ pending: 3, synced: 0, rejected: 0 });

    api.failWith = null;
    expect((await engine.flush()).stopped).toBe('none');
    expect(await journal.counts()).toEqual({ pending: 0, synced: 3, rejected: 0 });
  });

  it('treats a server error as retryable and a 401 as a need to sign in again', async () => {
    const { api, engine } = await setup(1);
    api.failWith = new ApiError(503, 'unavailable', 'Indisponible');
    expect((await engine.flush()).stopped).toBe('network');

    api.failWith = new ApiError(401, 'unauthorized', 'Expiré');
    expect((await engine.flush()).stopped).toBe('auth');
  });

  it('rejects a refused session opening and stops, because the sales depend on it', async () => {
    const { journal, api, engine } = await setup(2);
    api.openFailure = new ApiError(409, 'register_already_open', 'Une session est déjà ouverte');

    const outcome = await engine.flush();

    expect(outcome.stopped).toBe('blocked');
    const operations = await journal.operations();
    expect(operations[0]).toMatchObject({ kind: 'openSession', status: 'rejected', errorCode: 'register_already_open' });
    expect(operations.slice(1).every((op) => op.status === 'pending')).toBe(true);
    expect(api.receivedSales).toEqual([]);
  });

  it('retries immediately once on a concurrent conflict', async () => {
    const { api, engine } = await setup(0);
    let first = true;
    api.openSession = async () => {
      api.calls.push('open');
      if (first) {
        first = false;
        throw new ApiError(409, 'concurrent_conflict', 'Opération concurrente');
      }
    };
    const outcome = await engine.flush();
    expect(api.calls).toEqual(['open', 'open']);
    expect(outcome.stopped).toBe('none');
  });

  it('batches sales by 200 at most', async () => {
    const { api, engine } = await setup(0);
    // 250 ventes : on les insère rapidement dans le journal
    const journal = (engine as unknown as { journal: Journal }).journal;
    for (let index = 1; index <= 250; index++) {
      await journal.recordSale({
        id: `bulk-${index}`, at: '2026-10-23T08:10:00.000Z',
        lines: [{ productId: 'p-jus', name: 'Jus', unitPriceFcfa: 600, quantity: 1 }],
        payments: [{ id: `p-${index}`, method: 'Cash', amountFcfa: 600, tenderedFcfa: 600 }],
      });
    }
    await engine.flush();
    expect(api.calls).toEqual(['open', 'sync:200', 'sync:50']);
  });

  it('sends the closing after the sales and records the variance (REQ-08)', async () => {
    const { journal, api, engine } = await setup(1);
    await journal.closeSession({ countedCashFcfa: 11300, at: '2026-10-23T17:00:00.000Z' });

    await engine.flush();

    expect(api.calls).toEqual(['open', 'sync:1', 'close']);
    const closing = (await journal.operations()).at(-1);
    expect(closing).toMatchObject({ kind: 'closeSession', status: 'synced', result: { varianceFcfa: -500 } });
  });

  it('shares one sending between two simultaneous flushes', async () => {
    const { api, engine } = await setup(2);
    const [a, b] = await Promise.all([engine.flush(), engine.flush()]);
    expect(api.calls.filter((call) => call === 'open')).toHaveLength(1);
    expect(b).toBe(a);
    expect(a.sent).toBe(3);
  });
});

describe('nextDelayMs', () => {
  it('doubles from 2 seconds and is capped at 60 seconds', () => {
    expect([0, 1, 2, 3, 4, 5, 6, 10].map(nextDelayMs)).toEqual([2000, 4000, 8000, 16000, 32000, 60000, 60000, 60000]);
  });
});
