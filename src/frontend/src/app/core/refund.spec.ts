import { estimateRefund, RefundSale } from './refund';

const sale = (overrides: Partial<RefundSale> = {}): RefundSale => ({
  lines: [{ quantity: 2, totalFcfa: 2000 }],
  grossTotalFcfa: 2000,
  discountFcfa: 0,
  payments: [{ method: 'Cash', amountFcfa: 2000 }],
  returns: [],
  ...overrides,
});

describe('estimateRefund (ARR-03, ARR-04)', () => {
  it('refunds the price of one article of two, in cash', () => {
    expect(estimateRefund(sale(), [{ lineIndex: 0, quantity: 1 }])).toEqual({ totalFcfa: 1000, parts: [{ method: 'Cash', amountFcfa: 1000 }] });
  });

  it('shares the discount of the sale over the articles that come back', () => {
    const discounted = sale({ discountFcfa: 200, payments: [{ method: 'Cash', amountFcfa: 1800 }] });

    expect(estimateRefund(discounted, [{ lineIndex: 0, quantity: 1 }]).totalFcfa).toBe(900);
    expect(estimateRefund(discounted, [{ lineIndex: 0, quantity: 2 }]).totalFcfa).toBe(1800);
  });

  it('adds up exactly to what was paid whatever the rounding, return after return', () => {
    const odd = sale({ lines: [{ quantity: 3, totalFcfa: 1000 }], grossTotalFcfa: 1000, payments: [{ method: 'Cash', amountFcfa: 1000 }] });
    let returns: RefundSale['returns'] = [];
    let total = 0;
    for (let step = 0; step < 3; step++) {
      const part = estimateRefund({ ...odd, returns }, [{ lineIndex: 0, quantity: 1 }]);
      total += part.totalFcfa;
      returns = [...returns, { lines: [{ lineIndex: 0, quantity: 1 }], refund: part.parts }];
    }

    expect(total).toBe(1000);
  });

  it('refunds a weighed product for the exact weight, rounded to the franc', () => {
    const rice = sale({ lines: [{ quantity: 2000, totalFcfa: 1600 }], grossTotalFcfa: 1600, payments: [{ method: 'Cash', amountFcfa: 1600 }] });

    expect(estimateRefund(rice, [{ lineIndex: 0, quantity: 500 }]).totalFcfa).toBe(400);
    expect(estimateRefund(rice, [{ lineIndex: 0, quantity: 333 }]).totalFcfa).toBe(266);
  });

  it('starts with the debt of the customer, then the cash, then the mobile money', () => {
    const mixed = sale({
      payments: [{ method: 'Wave', amountFcfa: 700 }, { method: 'Cash', amountFcfa: 800 }, { method: 'Credit', amountFcfa: 500 }],
    });

    expect(estimateRefund(mixed, [{ lineIndex: 0, quantity: 2 }]).parts).toEqual([
      { method: 'Credit', amountFcfa: 500 },
      { method: 'Cash', amountFcfa: 800 },
      { method: 'Wave', amountFcfa: 700 },
    ]);
    expect(estimateRefund(mixed, [{ lineIndex: 0, quantity: 1 }]).parts).toEqual([
      { method: 'Credit', amountFcfa: 500 },
      { method: 'Cash', amountFcfa: 500 },
    ]);
  });

  it('never gives back a method more than it paid, counting the earlier returns', () => {
    const mixed = sale({
      payments: [{ method: 'Cash', amountFcfa: 1000 }, { method: 'Wave', amountFcfa: 1000 }],
      returns: [{ lines: [{ lineIndex: 0, quantity: 1 }], refund: [{ method: 'Cash', amountFcfa: 1000 }] }],
    });

    expect(estimateRefund(mixed, [{ lineIndex: 0, quantity: 1 }]).parts).toEqual([{ method: 'Wave', amountFcfa: 1000 }]);
  });

  it('returns nothing to refund for an empty request', () => {
    expect(estimateRefund(sale(), [])).toEqual({ totalFcfa: 0, parts: [] });
  });
});
