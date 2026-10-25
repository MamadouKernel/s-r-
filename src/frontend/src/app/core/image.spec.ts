import { fitWithin, MAX_SIDE_PX } from './image';

describe('fitWithin', () => {
  it('leaves a small image alone', () => {
    expect(fitWithin(300, 200, MAX_SIDE_PX)).toEqual({ width: 300, height: 200 });
  });

  it('shrinks the longest side to the limit and keeps the proportions', () => {
    expect(fitWithin(4000, 3000, 480)).toEqual({ width: 480, height: 360 });
    expect(fitWithin(3000, 4000, 480)).toEqual({ width: 360, height: 480 });
  });

  it('never gives a side of zero pixel', () => {
    expect(fitWithin(10000, 10, 480)).toEqual({ width: 480, height: 1 });
  });
});
