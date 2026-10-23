import { describeVariance } from './variance';

describe('describeVariance', () => {
  it('says shortfall, surplus or no gap in words', () => {
    expect(describeVariance(-500)).toBe('Manque de 500 FCFA');
    expect(describeVariance(1200)).toBe('Excédent de 1 200 FCFA');
    expect(describeVariance(0)).toBe('Aucun écart');
  });
});
