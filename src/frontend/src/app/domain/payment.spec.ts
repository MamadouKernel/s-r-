import { cashChange, referenceKey, validatePayments } from './payment';
import { PaymentDraft } from './types';

const cash = (amount: number, tendered = amount): PaymentDraft => ({
  id: 'c' + amount, method: 'Cash', amountFcfa: amount, tenderedFcfa: tendered,
});
const momo = (amount: number, reference: string, method: PaymentDraft['method'] = 'OrangeMoney'): PaymentDraft => ({
  id: 'm' + amount + reference, method, amountFcfa: amount, reference,
});

describe('cashChange', () => {
  it('gives the change to return', () => {
    expect(cashChange(1800, 2000)).toBe(200);
    expect(cashChange(1800, 1800)).toBe(0);
  });

  it('refuses a tendered amount below what is due', () => {
    expect(() => cashChange(1800, 1500)).toThrow();
  });
});

describe('referenceKey', () => {
  it('is case insensitive and trimmed, per operator, like the server', () => {
    expect(referenceKey('OrangeMoney', ' mp-100 ')).toBe('OrangeMoney:MP-100');
    expect(referenceKey('Wave', 'mp-100')).not.toBe(referenceKey('OrangeMoney', 'mp-100'));
  });
});

describe('validatePayments', () => {
  const none = new Set<string>();

  it('accepts a single cash payment that covers the total', () => {
    expect(validatePayments(1800, [cash(1800, 2000)], none)).toEqual([]);
  });

  it('accepts a mixed payment whose parts sum exactly to the total', () => {
    expect(validatePayments(2500, [cash(1000), momo(1500, 'MP-1')], none)).toEqual([]);
  });

  it('refuses payments that do not sum to the total', () => {
    expect(validatePayments(2500, [cash(1000), momo(1000, 'MP-1')], none)).toContain(
      'Le total des paiements doit être égal au montant à payer.',
    );
  });

  it('refuses a sale without any payment', () => {
    expect(validatePayments(1000, [], none).length).toBeGreaterThan(0);
  });

  it('requires a Mobile Money reference', () => {
    expect(validatePayments(1000, [momo(1000, '  ')], none)).toContain(
      'La référence de la transaction est obligatoire.',
    );
  });

  it('refuses a reference already used on this device (SC-P5)', () => {
    const used = new Set([referenceKey('OrangeMoney', 'MP-9')]);
    expect(validatePayments(1000, [momo(1000, 'mp-9')], used)).toContain('La référence mp-9 a déjà été utilisée.');
  });

  it('allows the same reference with another operator', () => {
    const used = new Set([referenceKey('OrangeMoney', 'MP-9')]);
    expect(validatePayments(1000, [momo(1000, 'MP-9', 'Wave')], used)).toEqual([]);
  });

  it('refuses the same reference twice inside one sale', () => {
    expect(validatePayments(2000, [momo(1000, 'MP-1'), momo(1000, 'mp-1')], none).length).toBeGreaterThan(0);
  });

  it('refuses a cash payment where the client gave less than the amount', () => {
    expect(validatePayments(1000, [cash(1000, 500)], none).length).toBeGreaterThan(0);
  });

  it('refuses zero or fractional amounts', () => {
    expect(validatePayments(1000, [cash(0)], none).length).toBeGreaterThan(0);
    expect(validatePayments(1000.5, [cash(1000.5)], none).length).toBeGreaterThan(0);
  });

  it('does not offer credit while customers do not exist', () => {
    const credit: PaymentDraft = { id: 'k', method: 'Credit', amountFcfa: 1000 };
    expect(validatePayments(1000, [credit], none).length).toBeGreaterThan(0);
  });
});
