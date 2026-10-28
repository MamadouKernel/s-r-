import { COUNT_TOLERANCE_FCFA, DENOMINATIONS, needsRecount, sumDenominations, DRAWER_WARNING_FCFA, CASH_OUT_APPROVAL_FCFA } from './cash-count';

describe('comptage de caisse (CLO-04, CLO-09)', () => {
  it('knows the coins and notes of the franc, biggest first, each value once', () => {
    expect(DENOMINATIONS.map((item) => item.value)).toEqual([10_000, 5_000, 2_000, 1_000, 500, 250, 200, 100, 50, 25, 10, 5]);
    expect(new Set(DENOMINATIONS.map((item) => item.value)).size).toBe(DENOMINATIONS.length);
  });

  it('adds up what was counted by value and ignores empty lines', () => {
    expect(sumDenominations({ 5000: 2, 1000: 3, 500: 1, 100: 0 })).toBe(13_500);
    expect(sumDenominations({})).toBe(0);
  });

  it('asks for a recount beyond the tolerance, in both directions, and never within it', () => {
    expect(COUNT_TOLERANCE_FCFA).toBe(500);
    expect(needsRecount(12_000, 10_000)).toBe(true);
    expect(needsRecount(8_000, 10_000)).toBe(true);
    expect(needsRecount(10_500, 10_000)).toBe(false);
    expect(needsRecount(9_500, 10_000)).toBe(false);
    expect(needsRecount(10_501, 10_000)).toBe(true);
  });

  it('keeps the starting thresholds of the shop, the same as the server', () => {
    expect(DRAWER_WARNING_FCFA).toBe(100_000);
    expect(CASH_OUT_APPROVAL_FCFA).toBe(5_000);
  });
});
