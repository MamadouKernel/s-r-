import { averageBasket, describeVariation, hoursOpen, isStaleSession, paymentLabel, paymentShares, variation } from './metrics';

describe('averageBasket', () => {
  it('divides the total by the number of tickets, rounded to the franc', () => {
    expect(averageBasket(2500, 2)).toBe(1250);
    expect(averageBasket(1000, 3)).toBe(333);
    expect(averageBasket(2000, 3)).toBe(667);
  });

  it('has no value without any sale', () => {
    expect(averageBasket(0, 0)).toBeNull();
  });
});

describe('variation', () => {
  it('detects a rise, a fall and a stable value', () => {
    expect(variation(1120, 1000)).toEqual({ kind: 'up', percent: 12 });
    expect(variation(920, 1000)).toEqual({ kind: 'down', percent: 8 });
    expect(variation(1000, 1000)).toEqual({ kind: 'stable', percent: 0 });
  });

  it('treats a change under half a percent as stable', () => {
    expect(variation(10_040, 10_000).kind).toBe('stable');
  });

  it('has no comparison when yesterday was at zero', () => {
    expect(variation(500, 0)).toEqual({ kind: 'none', percent: 0 });
    expect(variation(0, 0).kind).toBe('none');
  });

  it('handles a day that fell to zero', () => {
    expect(variation(0, 1000)).toEqual({ kind: 'down', percent: 100 });
  });
});

describe('describeVariation', () => {
  it('says it in words, never by sign or colour alone (SC-D2)', () => {
    expect(describeVariation({ kind: 'up', percent: 12 })).toBe('en hausse de 12 % par rapport à hier à la même heure');
    expect(describeVariation({ kind: 'down', percent: 8 })).toBe('en baisse de 8 % par rapport à hier à la même heure');
    expect(describeVariation({ kind: 'stable', percent: 0 })).toBe('stable par rapport à hier à la même heure');
    expect(describeVariation({ kind: 'none', percent: 0 })).toBe('pas de vente hier à la même heure pour comparer');
  });
});

describe('paymentShares', () => {
  it('orders by amount and gives each share of the total (SC-D1)', () => {
    const shares = paymentShares([
      { method: 'Cash', amountFcfa: 2000, count: 2 },
      { method: 'OrangeMoney', amountFcfa: 6000, count: 1 },
      { method: 'Wave', amountFcfa: 2000, count: 1 },
    ]);
    expect(shares.map((share) => [share.method, share.percent])).toEqual([
      ['OrangeMoney', 60], ['Cash', 20], ['Wave', 20],
    ]);
  });

  it('is empty without payments, with no division by zero', () => {
    expect(paymentShares([])).toEqual([]);
    expect(paymentShares([{ method: 'Cash', amountFcfa: 0, count: 0 }])[0].percent).toBe(0);
  });
});

describe('paymentLabel', () => {
  it('names each method in French', () => {
    expect(['Cash', 'OrangeMoney', 'MtnMomo', 'MoovMoney', 'Wave', 'Credit'].map(paymentLabel)).toEqual([
      'Espèces', 'Orange Money', 'MTN MoMo', 'Moov Money', 'Wave', 'Crédit',
    ]);
    expect(paymentLabel('Chèque')).toBe('Chèque');
  });
});

describe('open sessions', () => {
  const now = new Date('2026-10-23T18:00:00.000Z');

  it('counts the hours a register has been open', () => {
    expect(hoursOpen('2026-10-23T16:30:00.000Z', now)).toBe(1.5);
    expect(hoursOpen('2026-10-23T18:30:00.000Z', now)).toBe(0); // jamais négatif
  });

  it('flags a register open for more than 12 hours (SC-D3)', () => {
    expect(isStaleSession('2026-10-23T04:00:00.000Z', now)).toBe(true); // 14 h
    expect(isStaleSession('2026-10-23T06:00:00.000Z', now)).toBe(false); // 12 h pile
    expect(isStaleSession('2026-10-23T12:00:00.000Z', now)).toBe(false);
  });
});
