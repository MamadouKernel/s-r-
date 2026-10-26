import { TestBed } from '@angular/core/testing';
import { SaleOperation } from '../data/journal';
import { AuthService } from './auth.service';
import { JournalService } from './journal.service';
import { ReceiptService } from './receipt.service';

const at = (hour: number) => new Date(2026, 9, 25, hour, 0).toISOString();

const sale = (hour: number, id = 's'): SaleOperation =>
  ({
    kind: 'sale', seq: hour, status: 'synced', saleId: id, sessionId: 'sess', at: at(hour),
    lines: [{ productId: 'a', name: 'Pain de mie', unitPriceFcfa: 500, quantity: 1 }], discountFcfa: 0,
    payments: [{ id: 'p', method: 'Cash', amountFcfa: 500, tenderedFcfa: 500 }], netTotalFcfa: 500, changeFcfa: 0,
  }) as SaleOperation;

function setup(options: { device?: object | null; operations?: unknown[] } = {}) {
  const operations = options.operations ?? [sale(9), sale(11), sale(14)];
  TestBed.resetTestingModule();
  TestBed.configureTestingModule({
    providers: [
      { provide: AuthService, useValue: { device: () => (options.device === undefined ? { name: 'Caisse du fond', commerceName: 'Supérette du Carrefour' } : options.device), user: () => ({ id: 'u', displayName: 'Awa Koné', role: 'Cashier' }) } },
      { provide: JournalService, useValue: { get: async () => ({ operations: async () => operations }) } },
    ],
  });
  return TestBed.inject(ReceiptService);
}

describe('ReceiptService', () => {
  it('builds the ticket of a sale from what the device knows, and keeps it as the last one', async () => {
    const receipts = setup();
    expect(receipts.last()).toBeNull();

    const receipt = await receipts.remember(sale(14), null);

    expect(receipt.commerce).toBe('Supérette du Carrefour');
    expect(receipt.cashier).toBe('Awa Koné');
    expect(receipt.register).toBe('Caisse du fond');
    expect(receipt.rank).toBe(3);
    expect(receipts.last()).toBe(receipt);
  });

  it('counts only the sales of the same day', async () => {
    const yesterday = { ...sale(9, 'old'), at: new Date(2026, 9, 24, 23, 0).toISOString() };
    const receipts = setup({ operations: [yesterday, sale(9), sale(14), { kind: 'openSession', seq: 1, at: at(8) }] });

    expect((await receipts.remember(sale(14), null)).rank).toBe(2);
  });

  it('gives sensible names when the device has none (a browser where only the owner signed in)', async () => {
    const receipt = await setup({ device: null }).remember(sale(14), null);

    expect(receipt.commerce).toBe('Boutique');
    expect(receipt.register).toBe('Caisse');
  });

  it('puts the customer of a credit sale on the ticket', async () => {
    const credit = { ...sale(14), payments: [{ id: 'c', method: 'Credit' as const, amountFcfa: 500 }] };

    const receipt = await setup().remember(credit as SaleOperation, 'Awa Traoré');

    expect(receipt.customer).toEqual({ name: 'Awa Traoré', creditFcfa: 500 });
  });

  it('replaces the last ticket with the next sale', async () => {
    const receipts = setup();

    await receipts.remember(sale(11), null);
    const second = await receipts.remember(sale(14), null);

    expect(receipts.last()).toBe(second);
  });
});
