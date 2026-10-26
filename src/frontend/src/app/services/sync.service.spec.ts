import 'fake-indexeddb/auto';
import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Journal, SaleOperation } from '../data/journal';
import { ApiError, SaleSyncStatus } from '../sync/sync-api';
import { ApiService } from './api.service';
import { AuthService } from './auth.service';
import { JournalService } from './journal.service';
import { NetworkService } from './network.service';
import { SyncService } from './sync.service';

async function setup(sales = 2) {
  const journal = await Journal.open(`soro-svc-${Math.random()}`);
  await journal.replaceCatalogue([
    { id: 'p', name: 'Jus', barcode: '1', category: null, salePriceFcfa: 600, tracksStock: true, quantityOnHand: 50 },
  ]);
  await journal.openSession({
    sessionId: 's', pointOfSaleId: 'pos', registerId: 'reg', openingFloatFcfa: 0, openedAt: '2026-10-23T08:00:00.000Z',
  });
  for (let index = 0; index < sales; index++) {
    await journal.recordSale({
      id: 'sale-' + index, at: '2026-10-23T08:10:00.000Z',
      lines: [{ productId: 'p', name: 'Jus', unitPriceFcfa: 600, quantity: 1 }],
      payments: [{ id: 'pay' + index, method: 'Cash', amountFcfa: 600, tenderedFcfa: 600 }],
    });
  }
  const online = signal(true);
  const signedIn = signal(true);
  const api = {
    failure: null as ApiError | null,
    openSession: vi.fn(async () => {
      if (api.failure) throw api.failure;
    }),
    syncSales: vi.fn(async (batch: SaleOperation[]): Promise<SaleSyncStatus[]> => {
      if (api.failure) throw api.failure;
      return batch.map((sale) => ({ saleId: sale.saleId, outcome: 'Accepted' as const }));
    }),
    closeSession: vi.fn(),
    sendOfflineLogins: vi.fn(async () => {
      if (api.failure) throw api.failure;
    }),
  };
  TestBed.configureTestingModule({
    providers: [
      { provide: JournalService, useValue: { get: () => Promise.resolve(journal) } },
      { provide: ApiService, useValue: api },
      { provide: NetworkService, useValue: { online } },
      { provide: AuthService, useValue: { signedIn, tick: () => undefined } },
    ],
  });
  return { sync: TestBed.inject(SyncService), journal, api, online, signedIn };
}

describe('SyncService', () => {
  afterEach(() => vi.useRealTimers());

  it('sends everything when online and signed in, then goes idle with an up to date banner', async () => {
    const { sync, journal } = await setup();
    await sync.requestFlush();
    expect(await journal.counts()).toEqual({ pending: 0, synced: 3, rejected: 0 });
    expect(sync.state()).toBe('idle');
    expect(sync.banner()).toEqual({ tone: 'ok', text: 'En ligne · tout est synchronisé' });
  });

  it('waits offline and shows the number of pending operations in the banner', async () => {
    const { sync, api, online } = await setup(3);
    online.set(false);
    await sync.requestFlush();
    expect(api.syncSales).not.toHaveBeenCalled();
    expect(sync.state()).toBe('waiting');
    expect(sync.banner()).toEqual({ tone: 'warning', text: 'Hors ligne · 4 en attente de synchronisation' });
  });

  it('asks to sign in again when the token is no longer valid, without sending', async () => {
    const { sync, api, signedIn } = await setup();
    signedIn.set(false);
    await sync.requestFlush();
    expect(api.openSession).not.toHaveBeenCalled();
    expect(sync.state()).toBe('auth');
    expect(sync.banner().text).toContain('reconnectez-vous');
  });

  it('retries after a delay following a network failure, then recovers', async () => {
    const { sync, api, journal } = await setup(1);
    api.failure = new ApiError(0, 'network', 'down');
    vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout'] });
    await sync.requestFlush();
    expect(sync.state()).toBe('waiting');
    expect((await journal.counts()).pending).toBe(2);

    api.failure = null;
    await vi.advanceTimersByTimeAsync(2_000);
    await vi.waitFor(async () => expect((await journal.counts()).pending).toBe(0));
    expect(sync.state()).toBe('idle');
  });

  it('shows rejected operations as an error in the banner', async () => {
    const { sync, api, journal } = await setup(1);
    api.syncSales.mockImplementationOnce(async (batch) =>
      batch.map((sale) => ({ saleId: sale.saleId, outcome: 'Rejected' as const, errorCode: 'x', message: 'refusée' })),
    );
    await sync.requestFlush();
    expect((await journal.counts()).rejected).toBe(1);
    expect(sync.banner()).toEqual({ tone: 'error', text: '1 opération refusée par le serveur' });
  });

  it('sends the offline logins with the queue once signed in, then forgets them (HLC-10)', async () => {
    const { sync, journal, api } = await setup(0);
    await journal.saveOfflineLogins([{ eventId: 'e-1', userId: 'u-1', startedAt: '2026-10-26T08:02:00.000Z', endedAt: '2026-10-26T14:40:00.000Z' }]);

    await sync.requestFlush();

    expect(api.sendOfflineLogins).toHaveBeenCalledWith([{ eventId: 'e-1', userId: 'u-1', startedAt: '2026-10-26T08:02:00.000Z', endedAt: '2026-10-26T14:40:00.000Z' }]);
    expect(await journal.getOfflineLogins()).toEqual([]);
  });

  it('keeps them when the server cannot be reached, and does not send without a token or while a session is still running', async () => {
    const { sync, journal, api, signedIn } = await setup(0);
    await journal.saveOfflineLogins([
      { eventId: 'e-1', userId: 'u-1', startedAt: '2026-10-26T08:02:00.000Z', endedAt: '2026-10-26T14:40:00.000Z' },
      { eventId: 'e-2', userId: 'u-2', startedAt: '2026-10-26T15:00:00.000Z', endedAt: null },
    ]);
    signedIn.set(false);
    await sync.requestFlush();
    expect(api.sendOfflineLogins).not.toHaveBeenCalled();

    signedIn.set(true);
    api.failure = new ApiError(0, 'network', '');
    await sync.requestFlush();
    expect(await journal.getOfflineLogins()).toHaveLength(2);

    api.failure = null;
    await sync.requestFlush();
    expect(api.sendOfflineLogins).toHaveBeenLastCalledWith([expect.objectContaining({ eventId: 'e-1' })]);
    expect((await journal.getOfflineLogins()).map((event) => event.eventId)).toEqual(['e-2']);
  });
});
