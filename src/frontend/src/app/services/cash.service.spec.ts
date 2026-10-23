import 'fake-indexeddb/auto';
import { TestBed } from '@angular/core/testing';
import { Journal } from '../data/journal';
import { CatalogueProduct } from '../domain/types';
import { ApiService } from './api.service';
import { AuthService } from './auth.service';
import { CashService } from './cash.service';
import { CatalogueService } from './catalogue.service';
import { JournalService } from './journal.service';
import { SyncService } from './sync.service';

const jus: CatalogueProduct = {
  id: 'p-jus', name: 'Jus', barcode: '111', category: null, salePriceFcfa: 600, tracksStock: true, quantityOnHand: 24,
};

async function setup() {
  const journal = await Journal.open(`soro-cash-${Math.random()}`);
  await journal.replaceCatalogue([jus]);
  const requestFlush = vi.fn().mockResolvedValue(null);
  TestBed.configureTestingModule({
    providers: [
      { provide: JournalService, useValue: { get: () => Promise.resolve(journal) } },
      { provide: SyncService, useValue: { requestFlush } },
      { provide: ApiService, useValue: {} },
      { provide: AuthService, useValue: { device: () => ({ tenantId: 't', registerId: 'reg', pointOfSaleId: 'pos' }) } },
    ],
  });
  const cash = TestBed.inject(CashService);
  await TestBed.inject(CatalogueService).loadLocal();
  await cash.openSession(10000);
  return { cash, journal, requestFlush, catalogue: TestBed.inject(CatalogueService) };
}

/** Un second démarrage de l'application sur la même base locale (rechargement de la page). */
async function restart(journal: Journal): Promise<CashService> {
  TestBed.resetTestingModule();
  TestBed.configureTestingModule({
    providers: [
      { provide: JournalService, useValue: { get: () => Promise.resolve(journal) } },
      { provide: SyncService, useValue: { requestFlush: vi.fn().mockResolvedValue(null) } },
      { provide: ApiService, useValue: {} },
      { provide: AuthService, useValue: { device: () => ({ tenantId: 't', registerId: 'reg', pointOfSaleId: 'pos' }) } },
    ],
  });
  const cash = TestBed.inject(CashService);
  await cash.load();
  return cash;
}

describe('CashService', () => {
  it('opens a session from the device and asks for a synchronisation', async () => {
    const { cash, requestFlush } = await setup();
    expect(cash.session()).toMatchObject({ registerId: 'reg', pointOfSaleId: 'pos', openingFloatFcfa: 10000 });
    expect(requestFlush).toHaveBeenCalled();
  });

  it('adds by barcode, rejects an unknown code, and totals the cart', async () => {
    const { cash } = await setup();
    expect(await cash.addByBarcode('111')).toBe(true);
    expect(await cash.addByBarcode('111')).toBe(true);
    expect(await cash.addByBarcode('000')).toBe(false);
    expect(cash.cart()).toEqual([{ productId: 'p-jus', name: 'Jus', unitPriceFcfa: 600, quantity: 2 }]);
    expect(cash.total()).toBe(1200);
  });

  it('records the sale before clearing the cart and exposes the confirmation', async () => {
    const { cash, journal, requestFlush } = await setup();
    cash.add(jus, 3);

    const sale = await cash.confirmSale([{ id: 'pay-1', method: 'Cash', amountFcfa: 1800, tenderedFcfa: 2000 }]);

    expect(sale).toMatchObject({ status: 'pending', changeFcfa: 200, netTotalFcfa: 1800 });
    expect(cash.cart()).toEqual([]);
    expect(cash.lastSale()?.saleId).toBe(sale.saleId);
    expect((await journal.pendingOperations()).filter((op) => op.kind === 'sale')).toHaveLength(1);
    expect((await journal.findByBarcode('111'))?.quantityOnHand).toBe(21);
    expect(requestFlush).toHaveBeenCalledTimes(2);
  });

  it('records one sale only when the validate button is tapped twice (SC-P4)', async () => {
    const { cash, journal } = await setup();
    cash.add(jus);
    const payments = [{ id: 'pay-1', method: 'Cash' as const, amountFcfa: 600, tenderedFcfa: 600 }];

    const [first, second] = await Promise.all([cash.confirmSale(payments), cash.confirmSale(payments)]);

    expect(second.saleId).toBe(first.saleId);
    expect((await journal.operations()).filter((op) => op.kind === 'sale')).toHaveLength(1);
    expect(cash.submitting()).toBe(false);
  });

  it('keeps the cart and the same sale id after a refusal so that a new try cannot duplicate', async () => {
    const { cash, journal } = await setup();
    cash.add(jus);
    const wrong = [{ id: 'pay-1', method: 'Cash' as const, amountFcfa: 100, tenderedFcfa: 100 }];
    await expect(cash.confirmSale(wrong)).rejects.toThrow(/total des paiements/);
    expect(cash.cart()).toHaveLength(1);
    expect(cash.submitting()).toBe(false);

    const sale = await cash.confirmSale([{ id: 'pay-2', method: 'Cash', amountFcfa: 600, tenderedFcfa: 600 }]);
    expect(sale.netTotalFcfa).toBe(600);
    expect((await journal.operations()).filter((op) => op.kind === 'sale')).toHaveLength(1);
  });

  it('closes the session, empties the cart and queues the closing', async () => {
    const { cash, journal } = await setup();
    cash.add(jus);
    await cash.confirmSale([{ id: 'p', method: 'Cash', amountFcfa: 600, tenderedFcfa: 600 }]);
    expect(await cash.summary()).toEqual({ salesCount: 1, netTotalFcfa: 600, expectedCashFcfa: 10600 });

    const closing = await cash.closeSession(10600);

    expect(closing).toMatchObject({ kind: 'closeSession', status: 'pending' });
    expect(cash.session()).toBeNull();
    expect((await journal.pendingOperations()).at(-1)?.kind).toBe('closeSession');
  });

  it('keeps the cart after a reload of the page (SC-P8)', async () => {
    const { cash, journal } = await setup();
    cash.add(jus, 3);
    cash.changeQuantity('p-jus', 4);
    await cash.settle();

    const reloaded = await restart(journal);

    expect(reloaded.cart()).toEqual([{ productId: 'p-jus', name: 'Jus', unitPriceFcfa: 600, quantity: 4 }]);
    expect(reloaded.total()).toBe(2400);
  });

  it('forgets the cart once the sale is recorded, even after a reload', async () => {
    const { cash, journal } = await setup();
    cash.add(jus);
    await cash.confirmSale([{ id: 'p', method: 'Cash', amountFcfa: 600, tenderedFcfa: 600 }]);
    await cash.settle();

    expect((await restart(journal)).cart()).toEqual([]);
  });

  it('holds the cart to serve the next customer without creating a sale or touching the stock (SC-P9)', async () => {
    const { cash, journal } = await setup();
    cash.add(jus, 2);

    await cash.holdCurrent('Mme Koné');

    expect(cash.cart()).toEqual([]);
    expect(cash.held()).toHaveLength(1);
    expect(cash.held()[0]).toMatchObject({ label: 'Mme Koné', lines: [{ productId: 'p-jus', quantity: 2 }] });
    expect((await journal.operations()).filter((op) => op.kind === 'sale')).toHaveLength(0);
    expect((await journal.findByBarcode('111'))?.quantityOnHand).toBe(24);
  });

  it('gives a default label with the time when none is typed, and refuses an empty cart', async () => {
    const { cash } = await setup();
    await expect(cash.holdCurrent()).rejects.toThrow(/vide/);
    cash.add(jus);
    await cash.holdCurrent('   ');
    expect(cash.held()[0].label).toMatch(/^Vente de \d{2}:\d{2}$/);
  });

  it('resumes a held sale with its frozen prices, even if the catalogue price changed meanwhile', async () => {
    const { cash, journal } = await setup();
    cash.add(jus, 2);
    await cash.holdCurrent('A');
    await journal.replaceCatalogue([{ ...jus, salePriceFcfa: 700 }]);

    await cash.resume(cash.held()[0].id);

    expect(cash.cart()).toEqual([{ productId: 'p-jus', name: 'Jus', unitPriceFcfa: 600, quantity: 2 }]);
    expect(cash.held()).toEqual([]);
  });

  it('refuses to resume over a non empty cart and keeps both', async () => {
    const { cash } = await setup();
    cash.add(jus);
    await cash.holdCurrent('A');
    cash.add(jus, 5);
    await cash.settle();

    await expect(cash.resume(cash.held()[0].id)).rejects.toThrow(/panier en cours/);

    expect(cash.cart()[0].quantity).toBe(5);
    expect(cash.held()).toHaveLength(1);
  });

  it('keeps several held sales across a reload, and deletes one on request', async () => {
    const { cash, journal } = await setup();
    cash.add(jus);
    await cash.holdCurrent('A');
    cash.add(jus, 2);
    await cash.holdCurrent('B');

    const reloaded = await restart(journal);
    expect(reloaded.held().map((held) => held.label)).toEqual(['A', 'B']);

    await reloaded.discard(reloaded.held()[0].id);
    expect(reloaded.held().map((held) => held.label)).toEqual(['B']);
  });

  it('drops the held sales when the session is closed', async () => {
    const { cash } = await setup();
    cash.add(jus);
    await cash.holdCurrent('A');
    expect(cash.held()).toHaveLength(1);

    await cash.closeSession(10000);

    expect(cash.held()).toEqual([]);
  });
});
