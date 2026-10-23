import 'fake-indexeddb/auto';
import { Journal, SaleOperation } from './journal';
import { CatalogueProduct } from '../domain/types';

let counter = 0;
const jus: CatalogueProduct = {
  id: 'p-jus', name: "Jus d'orange 1L", barcode: '111', category: 'Boissons',
  salePriceFcfa: 600, tracksStock: true, quantityOnHand: 24,
};
const coupe: CatalogueProduct = {
  id: 'p-coupe', name: 'Coupe', barcode: null, category: 'Services',
  salePriceFcfa: 1500, tracksStock: false, quantityOnHand: 0,
};

async function newJournal(): Promise<Journal> {
  const journal = await Journal.open(`soro-test-${++counter}-${Math.random()}`);
  await journal.replaceCatalogue([jus, coupe]);
  return journal;
}

async function withOpenSession(journal: Journal): Promise<void> {
  await journal.openSession({
    sessionId: 's-1', pointOfSaleId: 'pos-1', registerId: 'reg-1', openingFloatFcfa: 10000,
    openedAt: '2026-10-23T08:00:00.000Z',
  });
}

const cashSale = (id: string, quantity = 3, tendered = 2000) => ({
  id,
  at: '2026-10-23T08:10:00.000Z',
  lines: [{ productId: 'p-jus', name: jus.name, unitPriceFcfa: 600, quantity }],
  payments: [{ id: 'pay-' + id, method: 'Cash' as const, amountFcfa: 600 * quantity, tenderedFcfa: tendered }],
});

describe('Journal: catalogue', () => {
  it('searches by name (case and accent insensitive) and finds an exact barcode', async () => {
    const journal = await newJournal();
    expect((await journal.listProducts('JUS')).map((p) => p.id)).toEqual(['p-jus']);
    expect((await journal.listProducts("jus d'oranGE")).map((p) => p.id)).toEqual(['p-jus']);
    expect((await journal.findByBarcode('111'))?.id).toBe('p-jus');
    expect(await journal.findByBarcode('999')).toBeNull();
    expect(await journal.listProducts()).toHaveLength(2);
  });

  it('replaces the catalogue without keeping removed products', async () => {
    const journal = await newJournal();
    await journal.replaceCatalogue([coupe]);
    expect((await journal.listProducts()).map((p) => p.id)).toEqual(['p-coupe']);
  });
});

describe('Journal: device and token', () => {
  it('keeps the device across reopenings of the database', async () => {
    const name = `soro-reopen-${Math.random()}`;
    const first = await Journal.open(name);
    await first.saveDevice({ tenantId: 't-1', registerId: 'reg-1', pointOfSaleId: 'pos-1' });
    const second = await Journal.open(name);
    expect(await second.getDevice()).toEqual({ tenantId: 't-1', registerId: 'reg-1', pointOfSaleId: 'pos-1' });
  });

  it('stores and clears the token', async () => {
    const journal = await newJournal();
    await journal.saveToken({ value: 'jwt', expiresAt: '2026-10-23T16:00:00.000Z', user: { id: 'u', displayName: 'Awa', role: 'Cashier' } });
    expect((await journal.getToken())?.value).toBe('jwt');
    await journal.clearToken();
    expect(await journal.getToken()).toBeNull();
  });
});

describe('Journal: session', () => {
  it('opens a session, queues the operation and refuses a second open session', async () => {
    const journal = await newJournal();
    await withOpenSession(journal);

    expect((await journal.currentSession())?.sessionId).toBe('s-1');
    const [operation] = await journal.pendingOperations();
    expect(operation).toMatchObject({ kind: 'openSession', status: 'pending', openingFloatFcfa: 10000 });
    await expect(withOpenSession(journal)).rejects.toThrow(/déjà ouverte/);
  });

  it('refuses a sale when no session is open (RG-01)', async () => {
    const journal = await newJournal();
    await expect(journal.recordSale(cashSale('sale-1'))).rejects.toThrow(/session/i);
  });
});

describe('Journal: sales (REQ-05)', () => {
  it('records a sale durably, with the change and a sequence after the session opening', async () => {
    const journal = await newJournal();
    await withOpenSession(journal);

    const sale = await journal.recordSale(cashSale('sale-1', 3, 2000));

    expect(sale).toMatchObject({ kind: 'sale', status: 'pending', netTotalFcfa: 1800, changeFcfa: 200, sessionId: 's-1' });
    expect(sale.seq).toBeGreaterThan((await journal.pendingOperations())[0].seq);
  });

  it('survives a reopening of the database (SC-P1)', async () => {
    const name = `soro-crash-${Math.random()}`;
    const journal = await Journal.open(name);
    await journal.replaceCatalogue([jus]);
    await withOpenSession(journal);
    await journal.recordSale(cashSale('a'));
    await journal.recordSale(cashSale('b'));
    await journal.recordSale(cashSale('c'));

    const reopened = await Journal.open(name);
    const pending = await reopened.pendingOperations();
    expect(pending.filter((op) => op.kind === 'sale')).toHaveLength(3);
    expect(pending.every((op) => op.status === 'pending')).toBe(true);
  });

  it('decrements the local stock of tracked products only (SC-P6)', async () => {
    const journal = await newJournal();
    await withOpenSession(journal);
    await journal.recordSale({
      id: 'mix', at: '2026-10-23T08:10:00.000Z',
      lines: [
        { productId: 'p-jus', name: jus.name, unitPriceFcfa: 600, quantity: 3 },
        { productId: 'p-coupe', name: coupe.name, unitPriceFcfa: 1500, quantity: 1 },
      ],
      payments: [{ id: 'p', method: 'Cash', amountFcfa: 3300, tenderedFcfa: 3300 }],
    });

    expect((await journal.findByBarcode('111'))?.quantityOnHand).toBe(21);
    expect((await journal.listProducts('coupe'))[0].quantityOnHand).toBe(0);
  });

  it('never blocks a sale on an empty stock', async () => {
    const journal = await newJournal();
    await withOpenSession(journal);
    await journal.recordSale(cashSale('big', 30, 18000));
    expect((await journal.findByBarcode('111'))?.quantityOnHand).toBe(-6);
  });

  it('records a double tap on the same sale id only once (SC-P4)', async () => {
    const journal = await newJournal();
    await withOpenSession(journal);

    const [first, second] = await Promise.all([journal.recordSale(cashSale('same')), journal.recordSale(cashSale('same'))]);

    expect(second.seq).toBe(first.seq);
    const sales = (await journal.pendingOperations()).filter((op) => op.kind === 'sale');
    expect(sales).toHaveLength(1);
    expect((await journal.findByBarcode('111'))?.quantityOnHand).toBe(21);
  });

  it('never gives the same sequence to two writers sharing the database (SC-P7)', async () => {
    const name = `soro-tabs-${Math.random()}`;
    const tabA = await Journal.open(name);
    const tabB = await Journal.open(name);
    await tabA.replaceCatalogue([jus]);
    await withOpenSession(tabA);

    const sales = await Promise.all(
      Array.from({ length: 10 }, (_, index) => (index % 2 === 0 ? tabA : tabB).recordSale(cashSale('s' + index, 1, 600))),
    );

    const sequences = sales.map((sale) => sale.seq);
    expect(new Set(sequences).size).toBe(10);
  });

  it('refuses payments that do not cover the total', async () => {
    const journal = await newJournal();
    await withOpenSession(journal);
    const sale = cashSale('short');
    sale.payments[0].amountFcfa = 1000;
    await expect(journal.recordSale(sale)).rejects.toThrow(/total des paiements/);
    expect((await journal.pendingOperations()).filter((op) => op.kind === 'sale')).toHaveLength(0);
  });

  it('refuses an empty cart', async () => {
    const journal = await newJournal();
    await withOpenSession(journal);
    await expect(journal.recordSale({ ...cashSale('empty'), lines: [] })).rejects.toThrow(/vide/);
  });

  it('refuses a Mobile Money reference already used on the device (SC-P5)', async () => {
    const journal = await newJournal();
    await withOpenSession(journal);
    const momo = (id: string, reference: string) => ({
      id, at: '2026-10-23T08:10:00.000Z',
      lines: [{ productId: 'p-jus', name: jus.name, unitPriceFcfa: 600, quantity: 1 }],
      payments: [{ id: 'p' + id, method: 'OrangeMoney' as const, amountFcfa: 600, reference }],
    });
    await journal.recordSale(momo('a', 'MP-100'));

    await expect(journal.recordSale(momo('b', 'mp-100'))).rejects.toThrow(/déjà été utilisée/);
    expect((await journal.usedReferences()).has('OrangeMoney:MP-100')).toBe(true);
  });

  it('keeps pending sales out of the stock when the catalogue is refreshed from the server', async () => {
    const journal = await newJournal();
    await withOpenSession(journal);
    await journal.recordSale(cashSale('a', 3));

    await journal.replaceCatalogue([{ ...jus, quantityOnHand: 24 }]);

    expect((await journal.findByBarcode('111'))?.quantityOnHand).toBe(21);
  });
});

describe('Journal: synchronisation states', () => {
  it('marks operations synced or rejected and keeps the rejection reason visible (SC-P3)', async () => {
    const journal = await newJournal();
    await withOpenSession(journal);
    const first = await journal.recordSale(cashSale('a'));
    const second = await journal.recordSale(cashSale('b'));

    await journal.markRejected(first.seq, 'mobile_money_reference_used', 'Référence déjà utilisée');
    await journal.markSynced(second.seq);

    const all = await journal.operations();
    const rejected = all.find((op) => op.seq === first.seq) as SaleOperation;
    expect(rejected).toMatchObject({ status: 'rejected', errorCode: 'mobile_money_reference_used', message: 'Référence déjà utilisée' });
    expect(all.find((op) => op.seq === second.seq)?.status).toBe('synced');
    expect(await journal.counts()).toEqual({ pending: 1, synced: 1, rejected: 1 });
  });

  it('keeps a rejected sale out of the used references', async () => {
    const journal = await newJournal();
    await withOpenSession(journal);
    const sale = await journal.recordSale({
      id: 'm', at: '2026-10-23T08:10:00.000Z',
      lines: [{ productId: 'p-jus', name: jus.name, unitPriceFcfa: 600, quantity: 1 }],
      payments: [{ id: 'pm', method: 'Wave', amountFcfa: 600, reference: 'W-1' }],
    });
    await journal.markRejected(sale.seq, 'x', 'y');
    expect((await journal.usedReferences()).size).toBe(0);
  });
});

describe('Journal: closing', () => {
  it('summarises the expected cash from the non rejected cash sales', async () => {
    const journal = await newJournal();
    await withOpenSession(journal);
    await journal.recordSale(cashSale('a', 3, 2000));
    const rejected = await journal.recordSale(cashSale('b', 1, 600));
    await journal.markRejected(rejected.seq, 'x', 'y');
    await journal.recordSale({
      id: 'm', at: '2026-10-23T08:10:00.000Z',
      lines: [{ productId: 'p-jus', name: jus.name, unitPriceFcfa: 600, quantity: 1 }],
      payments: [{ id: 'pm', method: 'Wave', amountFcfa: 600, reference: 'W-1' }],
    });

    expect(await journal.sessionSummary()).toEqual({
      salesCount: 2, netTotalFcfa: 2400, expectedCashFcfa: 10000 + 1800,
    });
  });

  it('queues the closing after the sales and then accepts no more sales', async () => {
    const journal = await newJournal();
    await withOpenSession(journal);
    const sale = await journal.recordSale(cashSale('a'));

    const closing = await journal.closeSession({ countedCashFcfa: 11500, at: '2026-10-23T17:00:00.000Z' });

    expect(closing.seq).toBeGreaterThan(sale.seq);
    expect(closing).toMatchObject({ kind: 'closeSession', status: 'pending', countedCashFcfa: 11500 });
    expect(await journal.currentSession()).toBeNull();
    await expect(journal.recordSale(cashSale('late'))).rejects.toThrow(/session/i);
  });
});
