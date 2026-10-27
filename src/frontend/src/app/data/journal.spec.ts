import 'fake-indexeddb/auto';
import { Journal, SaleOperation } from './journal';
import { CatalogueProduct, CustomerSnapshot } from '../domain/types';

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
      salesCount: 2, netTotalFcfa: 2400, expectedCashFcfa: 10000 + 1800, refundsFcfa: 0,
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

describe('Journal: durable cart and held sales (PWA-10)', () => {
  const lines = [
    { productId: 'p-jus', name: jus.name, unitPriceFcfa: 600, quantity: 2 },
    { productId: 'p-coupe', name: coupe.name, unitPriceFcfa: 1500, quantity: 1 },
  ];

  it('keeps the draft cart across a reopening of the database (SC-P8)', async () => {
    const name = `soro-draft-${Math.random()}`;
    const first = await Journal.open(name);
    await first.saveDraft(lines);

    expect(await (await Journal.open(name)).loadDraft()).toEqual(lines);
  });

  it('starts with an empty draft and clears it when saved empty', async () => {
    const journal = await newJournal();
    expect(await journal.loadDraft()).toEqual([]);
    await journal.saveDraft(lines);
    await journal.saveDraft([]);
    expect(await journal.loadDraft()).toEqual([]);
  });

  it('holds the cart, empties the draft and touches neither the queue nor the stock (SC-P9)', async () => {
    const journal = await newJournal();
    await withOpenSession(journal);
    await journal.saveDraft(lines);
    const before = (await journal.operations()).length;

    const held = await journal.holdCart({ id: 'h-1', label: 'Mme Koné', lines, createdAt: '2026-10-23T09:00:00.000Z' });

    expect(held).toMatchObject({ id: 'h-1', label: 'Mme Koné', lines });
    expect(await journal.loadDraft()).toEqual([]);
    expect(await journal.listHeld()).toHaveLength(1);
    expect((await journal.operations()).length).toBe(before);
    expect((await journal.findByBarcode('111'))?.quantityOnHand).toBe(24);
  });

  it('resumes a held cart with exactly its lines and prices, once, into the draft', async () => {
    const journal = await newJournal();
    await journal.holdCart({ id: 'h-1', label: 'A', lines, createdAt: '2026-10-23T09:00:00.000Z' });

    const resumed = await journal.resumeHeld('h-1');

    expect(resumed).toEqual(lines);
    expect(await journal.loadDraft()).toEqual(lines);
    expect(await journal.listHeld()).toEqual([]);
    await journal.saveDraft([]);
    await expect(journal.resumeHeld('h-1')).rejects.toThrow(/introuvable/);
  });

  it('refuses to resume while the current cart is not empty, and loses nothing', async () => {
    const journal = await newJournal();
    await journal.holdCart({ id: 'h-1', label: 'A', lines, createdAt: '2026-10-23T09:00:00.000Z' });
    await journal.saveDraft([lines[0]]);

    await expect(journal.resumeHeld('h-1')).rejects.toThrow(/panier en cours/);

    expect(await journal.listHeld()).toHaveLength(1);
    expect(await journal.loadDraft()).toEqual([lines[0]]);
  });

  it('lists held carts in the order they were held, even within the same millisecond, and deletes one on request', async () => {
    const journal = await newJournal();
    const sameInstant = '2026-10-23T09:00:00.000Z';
    await journal.holdCart({ id: 'first', label: 'A', lines, createdAt: sameInstant });
    await journal.holdCart({ id: 'second', label: 'B', lines, createdAt: sameInstant });
    await journal.holdCart({ id: 'third', label: 'C', lines, createdAt: sameInstant });

    expect((await journal.listHeld()).map((held) => held.id)).toEqual(['first', 'second', 'third']);
    await journal.deleteHeld('first');
    expect((await journal.listHeld()).map((held) => held.id)).toEqual(['second', 'third']);
  });

  it('does not consume the sending sequence when a cart is held', async () => {
    const journal = await newJournal();
    await withOpenSession(journal);
    await journal.holdCart({ id: 'h', label: 'A', lines, createdAt: '2026-10-23T09:00:00.000Z' });
    const sale = await journal.recordSale(cashSale('after-hold'));
    expect(sale.seq).toBe(2); // 1 = ouverture de session, 2 = la vente : la mise en attente n'a rien pris
  });

  it('refuses to hold an empty cart', async () => {
    const journal = await newJournal();
    await expect(journal.holdCart({ id: 'x', label: 'x', lines: [], createdAt: 'x' })).rejects.toThrow(/vide/);
  });

  it('purges the held carts and the draft when the session is closed, and reports how many', async () => {
    const journal = await newJournal();
    await withOpenSession(journal);
    await journal.holdCart({ id: 'h-1', label: 'A', lines, createdAt: '2026-10-23T09:00:00.000Z' });
    await journal.holdCart({ id: 'h-2', label: 'B', lines, createdAt: '2026-10-23T09:10:00.000Z' });
    await journal.saveDraft(lines);
    expect(await journal.heldCount()).toBe(2);

    await journal.closeSession({ countedCashFcfa: 10000, at: '2026-10-23T17:00:00.000Z' });

    expect(await journal.listHeld()).toEqual([]);
    expect(await journal.loadDraft()).toEqual([]);
  });
});

describe('Journal: database upgrade (SC-P10)', () => {
  it('opens a version 1 database in version 2 and keeps its journal, catalogue, device and token', async () => {
    const name = `soro-v1-${Math.random()}`;
    await new Promise<void>((resolve, reject) => {
      const request = indexedDB.open(name, 1);
      request.onupgradeneeded = () => {
        const db = request.result;
        const operations = db.createObjectStore('operations', { keyPath: 'seq' });
        operations.createIndex('saleId', 'saleId', { unique: true });
        operations.createIndex('status', 'status');
        db.createObjectStore('products', { keyPath: 'id' });
        db.createObjectStore('meta', { keyPath: 'key' });
      };
      request.onsuccess = () => {
        const db = request.result;
        const tx = db.transaction(['operations', 'products', 'meta'], 'readwrite');
        tx.objectStore('products').put(jus);
        tx.objectStore('meta').put({ key: 'device', value: { tenantId: 't', registerId: 'r', pointOfSaleId: 'p' } });
        tx.objectStore('meta').put({ key: 'sequence', value: 7 });
        tx.objectStore('operations').put({
          kind: 'openSession', seq: 7, status: 'pending', createdAt: 'x', sessionId: 's', pointOfSaleId: 'p',
          registerId: 'r', openingFloatFcfa: 5000, openedAt: 'x',
        });
        tx.oncomplete = () => {
          db.close();
          resolve();
        };
        tx.onerror = () => reject(tx.error);
      };
      request.onerror = () => reject(request.error);
    });

    const journal = await Journal.open(name);

    expect((await journal.getDevice())?.tenantId).toBe('t');
    expect((await journal.listProducts()).map((product) => product.id)).toEqual(['p-jus']);
    expect((await journal.operations()).map((operation) => operation.seq)).toEqual([7]);
    // La séquence continue après la dernière valeur de la version 1 et les nouvelles fonctions marchent.
    await journal.saveDraft([{ productId: 'p-jus', name: 'Jus', unitPriceFcfa: 600, quantity: 1 }]);
    expect(await journal.loadDraft()).toHaveLength(1);
    expect(await journal.listHeld()).toEqual([]);
  });
});

describe('Journal: customers and credit sales (CRD-04, offline)', () => {
  const awa: CustomerSnapshot = { id: 'c-awa', name: 'Awa Koné', phone: '+2250707070707', creditCeilingFcfa: 20_000, debtFcfa: 8_000, availableCreditFcfa: 12_000, isActive: true };
  const issa: CustomerSnapshot = { id: 'c-issa', name: 'Issa Diallo', phone: null, creditCeilingFcfa: 20_000, debtFcfa: 0, availableCreditFcfa: 20_000, isActive: true };
  const ancien: CustomerSnapshot = { ...issa, id: 'c-old', name: 'Ancien Client', isActive: false };

  const creditSale = (id: string, credit: number, customerId: string | null = 'c-awa') => ({
    id,
    at: '2026-10-23T08:10:00.000Z',
    lines: [{ productId: 'p-jus', name: jus.name, unitPriceFcfa: credit, quantity: 1 }],
    payments: [{ id: 'pay-' + id, method: 'Credit' as const, amountFcfa: credit }],
    customerId,
  });

  it('lists the active customers by name, searching names without accents or case and phones by their digits', async () => {
    const journal = await newJournal();
    await journal.replaceCustomers([issa, awa, ancien]);

    expect((await journal.listCustomers()).map((c) => c.name)).toEqual(['Awa Koné', 'Issa Diallo']);
    expect((await journal.listCustomers('KONE')).map((c) => c.id)).toEqual(['c-awa']);
    expect((await journal.listCustomers('07 07')).map((c) => c.id)).toEqual(['c-awa']);
    expect(await journal.listCustomers('zzz')).toEqual([]);
  });

  it('keeps the customer with the sale and raises the local debt at once, so the ceiling holds without network', async () => {
    const journal = await newJournal();
    await withOpenSession(journal);
    await journal.replaceCustomers([awa]);

    const sale = await journal.recordSale(creditSale('a', 5_000));

    expect(sale.customerId).toBe('c-awa');
    const [after] = await journal.listCustomers();
    expect(after.debtFcfa).toBe(13_000);
    expect(after.availableCreditFcfa).toBe(7_000);
  });

  it('accumulates several offline credit sales on the same customer, never below zero credit', async () => {
    const journal = await newJournal();
    await withOpenSession(journal);
    await journal.replaceCustomers([awa]);

    await journal.recordSale(creditSale('a', 5_000));
    await journal.recordSale(creditSale('b', 9_000));

    const [after] = await journal.listCustomers();
    expect(after.debtFcfa).toBe(22_000);
    expect(after.availableCreditFcfa).toBe(0);
  });

  it('refuses a credit sale without a customer and records nothing', async () => {
    const journal = await newJournal();
    await withOpenSession(journal);

    await expect(journal.recordSale(creditSale('a', 5_000, null))).rejects.toThrow('Choisissez le client');
    expect(await journal.counts()).toEqual({ pending: 1, synced: 0, rejected: 0 }); // seule l'ouverture de caisse
  });

  it('does not touch the debt for a cash sale, even when a customer is attached', async () => {
    const journal = await newJournal();
    await withOpenSession(journal);
    await journal.replaceCustomers([awa]);

    await journal.recordSale({ ...cashSale('a'), customerId: 'c-awa' });

    expect((await journal.listCustomers())[0].debtFcfa).toBe(8_000);
  });

  it('does not charge twice when the same sale is replayed', async () => {
    const journal = await newJournal();
    await withOpenSession(journal);
    await journal.replaceCustomers([awa]);

    await journal.recordSale(creditSale('a', 5_000));
    await journal.recordSale(creditSale('a', 5_000));

    expect((await journal.listCustomers())[0].debtFcfa).toBe(13_000);
  });

  it('adds the pending credit sales back when the list is refreshed from the server, until they are sent', async () => {
    const journal = await newJournal();
    await withOpenSession(journal);
    await journal.replaceCustomers([awa]);
    const sent = await journal.recordSale(creditSale('a', 5_000));

    await journal.replaceCustomers([awa]); // le serveur ne connaît pas encore cette vente

    const [pending] = await journal.listCustomers();
    expect(pending.debtFcfa).toBe(13_000);

    await journal.markSynced(sent.seq);
    await journal.replaceCustomers([{ ...awa, debtFcfa: 13_000, availableCreditFcfa: 7_000 }]); // le serveur l'a maintenant comptée

    expect((await journal.listCustomers())[0].debtFcfa).toBe(13_000);
  });

  it('adds or updates a single customer, for one created at the till', async () => {
    const journal = await newJournal();
    await journal.replaceCustomers([awa]);

    await journal.upsertCustomer(issa);
    await journal.upsertCustomer({ ...issa, name: 'Issa D.' });

    expect((await journal.listCustomers()).map((c) => c.name)).toEqual(['Awa Koné', 'Issa D.']);
  });
});

describe('Journal: remises et remboursements (docs/specs/annulations-remises-retours)', () => {
  const discounted = (id: string, discountFcfa: number, token?: string) => ({
    id,
    at: '2026-10-23T08:10:00.000Z',
    lines: [{ productId: 'p-jus', name: jus.name, unitPriceFcfa: 600, quantity: 3 }],
    discountFcfa,
    ...(token ? { discountApprovalToken: token } : {}),
    payments: [{ id: 'pay-' + id, method: 'Cash' as const, amountFcfa: 1800 - discountFcfa, tenderedFcfa: 2000 }],
  });

  it('records the discount, asks the payments to cover the total net of it, and keeps the approval of the manager', async () => {
    const journal = await newJournal();
    await withOpenSession(journal);

    const sale = await journal.recordSale(discounted('d-1', 300, 'sceau'));

    expect(sale.discountFcfa).toBe(300);
    expect(sale.netTotalFcfa).toBe(1500);
    expect(sale.changeFcfa).toBe(500);
    expect(sale.discountApprovalToken).toBe('sceau');
  });

  it('refuses payments that cover the total without the discount, and a discount as big as the sale', async () => {
    const journal = await newJournal();
    await withOpenSession(journal);

    await expect(journal.recordSale({ ...discounted('d-2', 300), payments: [{ id: 'p', method: 'Cash', amountFcfa: 1800, tenderedFcfa: 1800 }] })).rejects.toThrow(/total des paiements/);
    await expect(journal.recordSale({ ...discounted('d-3', 1800), payments: [] })).rejects.toThrow(/remise/i);
  });

  it('takes the cash given back to customers out of the cash expected, once per return', async () => {
    const journal = await newJournal();
    await withOpenSession(journal);
    await journal.recordSale(cashSale('a', 3, 1800));

    await journal.recordCashRefund({ returnId: 'r-1', amountFcfa: 600 });
    await journal.recordCashRefund({ returnId: 'r-1', amountFcfa: 600 });
    await journal.recordCashRefund({ returnId: 'r-2', amountFcfa: 0 });

    expect(await journal.sessionSummary()).toEqual({ salesCount: 1, netTotalFcfa: 1800, expectedCashFcfa: 10000 + 1800 - 600, refundsFcfa: 600 });
  });

  it('starts the refunds of a new session from zero', async () => {
    const journal = await newJournal();
    await withOpenSession(journal);
    await journal.recordCashRefund({ returnId: 'r-1', amountFcfa: 600 });
    await journal.closeSession({ countedCashFcfa: 9400, at: '2026-10-23T17:00:00.000Z' });
    await journal.openSession({ sessionId: 's-2', pointOfSaleId: 'pos-1', registerId: 'reg-1', openingFloatFcfa: 5000, openedAt: '2026-10-24T08:00:00.000Z' });

    expect((await journal.sessionSummary())?.refundsFcfa).toBe(0);
  });
});
