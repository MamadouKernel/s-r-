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
});
