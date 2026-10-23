import { lastDaysRange, startOfLocalDay, todayRange, yesterdaySameTimeRange } from './period';

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

describe('lastDaysRange', () => {
  const now = new Date(2026, 9, 23, 10, 42);

  it('is today alone for one day', () => {
    expect(lastDaysRange(now, 1)).toEqual(todayRange(now));
  });

  it('goes back to the local midnight of the first day, today included', () => {
    expect(lastDaysRange(now, 7)).toEqual({ from: new Date(2026, 9, 17).toISOString(), to: now.toISOString() });
    expect(lastDaysRange(now, 30).from).toBe(new Date(2026, 8, 24).toISOString());
  });

  it('crosses a month boundary and never gives an empty interval', () => {
    expect(lastDaysRange(new Date(2026, 10, 3, 9, 0), 7).from).toBe(new Date(2026, 9, 28).toISOString());
    const midnight = lastDaysRange(new Date(2026, 9, 23, 0, 0, 0, 0), 7);
    expect(Date.parse(midnight.to)).toBeGreaterThan(Date.parse(midnight.from));
  });

  it('treats a nonsensical day count as one day', () => {
    expect(lastDaysRange(now, 0)).toEqual(todayRange(now));
  });
});
