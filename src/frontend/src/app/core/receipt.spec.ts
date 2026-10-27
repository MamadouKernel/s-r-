import { SaleOperation } from '../data/journal';
import { buildReceipt, buildReversalReceipt, daySaleRank, Receipt, receiptDocument, receiptText } from './receipt';

const sale = (overrides: Partial<SaleOperation> = {}): SaleOperation =>
  ({
    kind: 'sale', seq: 1, status: 'pending', saleId: 's-1', sessionId: 'sess', at: '2026-10-25T14:32:00.000Z',
    lines: [
      { productId: 'a', name: "Jus d'orange 1L", unitPriceFcfa: 600, quantity: 2 },
      { productId: 'b', name: 'Pain de mie', unitPriceFcfa: 500, quantity: 1 },
    ],
    discountFcfa: 0, payments: [{ id: 'p', method: 'Cash', amountFcfa: 1700, tenderedFcfa: 2000 }], netTotalFcfa: 1700, changeFcfa: 300,
    ...overrides,
  }) as SaleOperation;

const context = { commerce: 'Supérette du Carrefour', cashier: 'Awa Koné', register: 'Caisse 1', rank: 12 };

const receiptOf = (overrides: Partial<SaleOperation> = {}, extra: Partial<typeof context & { customer: { name: string } | null }> = {}): Receipt =>
  buildReceipt(sale(overrides), { ...context, customer: null, ...extra });

describe('buildReceipt', () => {
  it('takes the lines, the total, the payments and the change from the sale', () => {
    const receipt = receiptOf();

    expect(receipt.commerce).toBe('Supérette du Carrefour');
    expect(receipt.cashier).toBe('Awa Koné');
    expect(receipt.lines).toEqual([
      { name: "Jus d'orange 1L", quantity: 2, unitPriceFcfa: 600, totalFcfa: 1200 },
      { name: 'Pain de mie', quantity: 1, unitPriceFcfa: 500, totalFcfa: 500 },
    ]);
    expect(receipt.totalFcfa).toBe(1700);
    expect(receipt.payments).toEqual([{ label: 'Espèces', amountFcfa: 1700 }]);
    expect(receipt.tenderedFcfa).toBe(2000);
    expect(receipt.changeFcfa).toBe(300);
    expect(receipt.customer).toBeNull();
  });

  it('names the payment methods in French and keeps a mixed payment in order', () => {
    const receipt = receiptOf({
      payments: [
        { id: '1', method: 'Cash', amountFcfa: 1000, tenderedFcfa: 1000 },
        { id: '2', method: 'Wave', amountFcfa: 400, reference: 'W123' },
        { id: '3', method: 'OrangeMoney', amountFcfa: 300, reference: 'O1' },
      ],
      changeFcfa: 0,
    });

    expect(receipt.payments.map((payment) => payment.label)).toEqual(['Espèces', 'Wave', 'Orange Money']);
  });

  it('shows what is left to pay for a credit sale, with the name of the customer', () => {
    const receipt = receiptOf(
      { payments: [{ id: '1', method: 'Cash', amountFcfa: 700, tenderedFcfa: 700 }, { id: '2', method: 'Credit', amountFcfa: 1000 }], changeFcfa: 0 },
      { customer: { name: 'Awa Traoré' } },
    );

    expect(receipt.customer).toEqual({ name: 'Awa Traoré', creditFcfa: 1000 });
    expect(receipt.payments.map((payment) => payment.label)).toEqual(['Espèces', 'Crédit']);
  });
});

describe('daySaleRank', () => {
  const day = (hour: number, minute = 0) => new Date(2026, 9, 25, hour, minute).toISOString();

  it('counts the sales of the same local day up to this one', () => {
    expect(daySaleRank(day(14), [day(8), day(11), day(14), day(16)])).toBe(3);
  });

  it('ignores the sales of another day', () => {
    expect(daySaleRank(day(9), [new Date(2026, 9, 24, 23, 59).toISOString(), day(9)])).toBe(1);
  });

  it('is at least 1', () => {
    expect(daySaleRank(day(9), [])).toBe(1);
  });
});

describe('receiptText', () => {
  it.each([[32], [42]])('never goes beyond %i columns, even with a very long name', (columns) => {
    const receipt = receiptOf({
      lines: [{ productId: 'a', name: 'Riz parfumé de très grande qualité sac de vingt-cinq kilogrammes', unitPriceFcfa: 17500, quantity: 3 }],
      netTotalFcfa: 52500, payments: [{ id: 'p', method: 'Cash', amountFcfa: 52500, tenderedFcfa: 55000 }], changeFcfa: 2500,
    });

    const lines = receiptText(receipt, columns).split('\n');

    expect(Math.max(...lines.map((line) => line.length))).toBeLessThanOrEqual(columns);
  });

  it('writes the whole ticket in French with the amounts on the right', () => {
    const text = receiptText(receiptOf(), 32);
    const lines = text.split('\n');

    expect(lines[0].trim()).toBe('Supérette du Carrefour');
    expect(lines[0].startsWith(' ')).toBe(true); // centré
    expect(text).toContain('Caissier : Awa Koné');
    expect(text).toContain('Caisse 1 · n° 12');
    expect(text).toContain("Jus d'orange 1L");
    expect(text).toMatch(/2 x 600 FCFA\s+1 200 FCFA/);
    expect(text).toMatch(/TOTAL\s+1 700 FCFA/);
    expect(text).toMatch(/Espèces\s+1 700 FCFA/);
    expect(text).toMatch(/Reçu\s+2 000 FCFA/);
    expect(text).toMatch(/Monnaie rendue\s+300 FCFA/);
    expect(text).toContain('Merci de votre visite');
    expect(text).toContain('ne vaut pas facture');
    expect(lines.filter((line) => /\s1 700 FCFA$/.test(line)).every((line) => line.length === 32)).toBe(true);
  });

  it('writes the date and time of the sale as the shop sees them', () => {
    const at = new Date(2026, 9, 25, 14, 32).toISOString();

    expect(receiptText(receiptOf({ at }), 32)).toContain('25/10/2026 14:32');
  });

  it('never uses a non-breaking space, which some printers draw badly', () => {
    expect(receiptText(receiptOf(), 32)).not.toContain(' ');
  });

  it('has no change line when there is none, and no received line for a mobile money payment', () => {
    const text = receiptText(receiptOf({ payments: [{ id: 'w', method: 'Wave', amountFcfa: 1700, reference: 'W1' }], changeFcfa: 0 }), 32);

    expect(text).not.toContain('Monnaie rendue');
    expect(text).not.toContain('Reçu');
    expect(text).toMatch(/Wave\s+1 700 FCFA/);
  });

  it('shows the discount when there is one', () => {
    const text = receiptText(receiptOf({ discountFcfa: 100, netTotalFcfa: 1600, payments: [{ id: 'p', method: 'Cash', amountFcfa: 1600, tenderedFcfa: 1600 }], changeFcfa: 0 }), 32);

    expect(text).toMatch(/Remise\s+-100 FCFA/);
    expect(text).toMatch(/TOTAL\s+1 600 FCFA/);
  });

  it('shows the customer and what is left to pay for a credit sale', () => {
    const text = receiptText(
      receiptOf({ payments: [{ id: 'c', method: 'Credit', amountFcfa: 1700 }], changeFcfa: 0 }, { customer: { name: 'Awa Traoré' } }),
      32,
    );

    expect(text).toContain('Client : Awa Traoré');
    expect(text).toMatch(/Reste à payer\s+1 700 FCFA/);
  });

  it('is the same ticket at 42 columns, with more room on the line', () => {
    const narrow = receiptText(receiptOf(), 32);
    const wide = receiptText(receiptOf(), 42);

    expect(wide).toContain('Merci de votre visite');
    expect(Math.max(...wide.split('\n').map((line) => line.length))).toBeGreaterThan(Math.max(...narrow.split('\n').map((line) => line.length)));
  });
});

describe('receipts of weighed products (docs/specs/vente-au-poids)', () => {
  it('writes the weight, the unit and the price of one kilo, and the rounded total of the line', () => {
    const weighed = receiptOf({
      lines: [{ productId: 'r', name: 'Riz parfumé', unitPriceFcfa: 800, quantity: 1250, unit: 'Kilogram' }],
      netTotalFcfa: 1000, payments: [{ id: 'p', method: 'Cash', amountFcfa: 1000, tenderedFcfa: 1000 }], changeFcfa: 0,
    });

    expect(weighed.lines).toEqual([{ name: 'Riz parfumé', quantity: 1250, unit: 'Kilogram', unitPriceFcfa: 800, totalFcfa: 1000 }]);
    expect(receiptText(weighed, 32)).toMatch(/1,250 kg x 800 FCFA\s+1 000 FCFA/);
  });

  it('keeps a piece line exactly as before and never writes a non-breaking space for a big weight', () => {
    const text = receiptText(
      receiptOf({ lines: [{ productId: 'r', name: 'Riz', unitPriceFcfa: 800, quantity: 1_234_567, unit: 'Kilogram' }], netTotalFcfa: 987_654, payments: [{ id: 'p', method: 'Cash', amountFcfa: 987_654 }], changeFcfa: 0 }),
      42,
    );

    expect(text).not.toContain('\u00a0');
    expect(text).toContain('1 234,567 kg x 800 FCFA');
    expect(receiptOf().lines[0]).not.toHaveProperty('unit');
  });
});

describe('receiptDocument', () => {
  it('is a whole page that prints the ticket at the right width, in a fixed-width font', () => {
    const html = receiptDocument(receiptOf(), 58);

    expect(html.startsWith('<!doctype html>')).toBe(true);
    expect(html).toContain('lang="fr"');
    expect(html).toContain('@page { margin: 0; }');
    expect(html).toContain('width: 48mm');
    expect(html).toContain('monospace');
    expect(html).toContain('Supérette du Carrefour');
    expect(html).toContain('Merci de votre visite');
  });

  it('is wider for an 80 mm roll', () => {
    expect(receiptDocument(receiptOf(), 80)).toContain('width: 72mm');
  });

  it('escapes what comes from the data so a name can never inject markup', () => {
    const html = receiptDocument(receiptOf({ lines: [{ productId: 'a', name: '<img src=x onerror=alert(1)>', unitPriceFcfa: 100, quantity: 1 }] }), 58);

    expect(html).not.toContain('<img');
    expect(html).toContain('&lt;img');
  });
});

describe('ticket de retour (docs/specs/annulations-remises-retours ARR-11)', () => {
  const reversal = (kind: 'Return' | 'Cancellation' = 'Return') =>
    buildReversalReceipt({
      commerce: 'Supérette du Carrefour', cashier: 'Awa Koné', register: 'Caisse 1', at: '2026-10-25T16:05:00.000Z', saleTicket: 12, kind,
      reason: 'Article en trop',
      lines: [{ name: "Jus d'orange 1L", quantity: 1, unitPriceFcfa: 600, totalFcfa: 600 }],
      refund: [{ method: 'Cash', amountFcfa: 400 }, { method: 'Credit', amountFcfa: 100 }, { method: 'Wave', amountFcfa: 100 }],
    });

  it('says what it is, which ticket it belongs to, what comes back and how it is paid back', () => {
    const lines = receiptText(reversal(), 32).split('\n');

    expect(lines.some((line) => line.trim() === 'TICKET DE RETOUR')).toBe(true);
    expect(lines).toContain("Ticket d'origine n° 12");
    expect(lines.some((line) => line.includes("Jus d'orange 1L"))).toBe(true);
    expect(lines.find((line) => line.startsWith('TOTAL RENDU'))).toMatch(/600 FCFA$/);
    expect(lines.find((line) => line.startsWith('Rendu en espèces'))).toMatch(/400 FCFA$/);
    expect(lines.find((line) => line.startsWith('Dette effacée'))).toMatch(/100 FCFA$/);
    expect(lines.find((line) => line.startsWith('À rembourser par Wave'))).toMatch(/100 FCFA$/);
    expect(lines.some((line) => line.includes('Motif : Article en trop'))).toBe(true);
  });

  it('is a cancellation notice when the whole sale is cancelled, and never overflows the paper', () => {
    const text = receiptText(reversal('Cancellation'), 32);

    expect(text).toContain('VENTE ANNULÉE');
    expect(text.split('\n').every((line) => line.length <= 32)).toBe(true);
    expect(text).not.toContain('Merci de votre visite');
    expect(text).not.toMatch(/\u00a0/);
  });

  it('leaves an ordinary ticket exactly as it was', () => {
    const text = receiptText(receiptOf(), 32);
    expect(text).not.toContain('TICKET DE RETOUR');
    expect(text).toContain('Merci de votre visite');
  });
});
