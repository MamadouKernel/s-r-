import { formatFcfa, parseFcfa } from './money';

// Espace insécable entre les milliers et avant « FCFA » : explicite pour qu'aucun éditeur ne la remplace.
const S = '\u00a0';

describe('formatFcfa', () => {
  it('groups thousands with a non-breaking narrow space and never shows decimals', () => {
    expect(formatFcfa(1500)).toBe(`1${S}500${S}FCFA`);
    expect(formatFcfa(1234567)).toBe(`1${S}234${S}567${S}FCFA`);
    expect(formatFcfa(0)).toBe(`0${S}FCFA`);
    expect(formatFcfa(999)).toBe(`999${S}FCFA`);
  });

  it('truncates a fractional input rather than showing decimals', () => {
    expect(formatFcfa(1500.9)).toBe(`1${S}500${S}FCFA`);
  });

  it('keeps the sign for a negative variance', () => {
    expect(formatFcfa(-500)).toBe(`-500${S}FCFA`);
  });
});

describe('parseFcfa', () => {
  it('reads digits, tolerating spaces', () => {
    expect(parseFcfa('1500')).toBe(1500);
    expect(parseFcfa(' 1 500 ')).toBe(1500);
    expect(parseFcfa('0')).toBe(0);
  });

  it('rejects empty, decimal, negative and non numeric input', () => {
    for (const bad of ['', '  ', '12,5', '12.5', '-3', 'abc', '1e3']) {
      expect(parseFcfa(bad)).toBeNull();
    }
  });
});
