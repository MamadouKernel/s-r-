import { startOfLocalDay, todayRange, yesterdaySameTimeRange } from './period';

describe('startOfLocalDay', () => {
  it('is the local midnight of the given moment', () => {
    const start = startOfLocalDay(new Date(2026, 9, 23, 10, 42, 17, 500));
    expect(start.getFullYear()).toBe(2026);
    expect(start.getMonth()).toBe(9);
    expect(start.getDate()).toBe(23);
    expect([start.getHours(), start.getMinutes(), start.getSeconds(), start.getMilliseconds()]).toEqual([0, 0, 0, 0]);
  });
});

describe('todayRange', () => {
  it('runs from the local midnight to now, as ISO instants', () => {
    const now = new Date(2026, 9, 23, 10, 42);
    expect(todayRange(now)).toEqual({ from: new Date(2026, 9, 23).toISOString(), to: now.toISOString() });
  });

  it('never gives an empty or inverted interval, even at midnight sharp (the server refuses it)', () => {
    const midnight = new Date(2026, 9, 23, 0, 0, 0, 0);
    const range = todayRange(midnight);
    expect(Date.parse(range.to)).toBeGreaterThan(Date.parse(range.from));
  });
});

describe('yesterdaySameTimeRange', () => {
  it('covers yesterday from its midnight up to the same elapsed time as today', () => {
    const now = new Date(2026, 9, 23, 10, 42);
    const range = yesterdaySameTimeRange(now);
    expect(range.from).toBe(new Date(2026, 9, 22).toISOString());
    expect(range.to).toBe(new Date(2026, 9, 22, 10, 42).toISOString());
  });

  it('is valid right after midnight too', () => {
    const range = yesterdaySameTimeRange(new Date(2026, 9, 23, 0, 0, 0, 0));
    expect(Date.parse(range.to)).toBeGreaterThan(Date.parse(range.from));
  });

  it('crosses a month boundary correctly', () => {
    const range = yesterdaySameTimeRange(new Date(2026, 10, 1, 9, 0));
    expect(range.from).toBe(new Date(2026, 9, 31).toISOString());
    expect(range.to).toBe(new Date(2026, 9, 31, 9, 0).toISOString());
  });
});
