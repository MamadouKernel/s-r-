import { amountToQuantity, formatQuantity, lineTotal, parseQuantity, parseThreshold, perUnit, quantityInput, quantityWords, scaleOf, unitSymbol } from './measure';

describe('scaleOf and unitSymbol', () => {
  it('counts a weighed or measured quantity in thousandths and a piece as it is', () => {
    expect([scaleOf('Piece'), scaleOf('Kilogram'), scaleOf('Litre')]).toEqual([1, 1000, 1000]);
    expect([unitSymbol('Piece'), unitSymbol('Kilogram'), unitSymbol('Litre')]).toEqual(['', 'kg', 'L']);
  });
});

describe('formatQuantity', () => {
  it('writes a weighed quantity with the french comma, three decimals and the unit', () => {
    expect(formatQuantity(1250, 'Kilogram')).toBe('1,250 kg');
    expect(formatQuantity(500, 'Litre')).toBe('0,500 L');
    expect(formatQuantity(25_500, 'Kilogram')).toBe('25,500 kg');
    expect(formatQuantity(1, 'Kilogram')).toBe('0,001 kg');
  });

  it('separates the thousands of a big quantity with a space', () => {
    expect(formatQuantity(1_234_567, 'Kilogram')).toBe('1 234,567 kg');
  });

  it('writes a number of pieces as it is, without a comma or a unit', () => {
    expect(formatQuantity(3, 'Piece')).toBe('3');
    expect(formatQuantity(3)).toBe('3');
  });

  it('writes a negative stock with its sign', () => {
    expect(formatQuantity(-750, 'Kilogram')).toBe('-0,750 kg');
  });
});

describe('parseQuantity', () => {
  it.each([
    ['1,5', 1500],
    ['1.5', 1500],
    ['1,250', 1250],
    ['0,333', 333],
    ['2', 2000],
    [' 0,5 ', 500],
    ['0,001', 1],
    ['25', 25_000],
    ['1 250,5', 1_250_500],
  ])('reads %j as %i thousandths', (text, expected) => expect(parseQuantity(text, 'Kilogram')).toEqual({ ok: true, quantity: expected }));

  it.each([
    ['', 'Écrivez un poids, par exemple 1,5.'],
    ['abc', 'Écrivez un poids, par exemple 1,5.'],
    ['1,2,3', 'Écrivez un poids, par exemple 1,5.'],
    ['-1', 'Écrivez un poids, par exemple 1,5.'],
    ['0', 'Le poids doit être supérieur à 0.'],
    ['0,000', 'Le poids doit être supérieur à 0.'],
    ['0,0625', 'Écrivez le poids avec trois décimales au plus.'],
    ['1,2345', 'Écrivez le poids avec trois décimales au plus.'],
  ])('refuses %j with a plain sentence', (text, message) => expect(parseQuantity(text, 'Kilogram')).toEqual({ ok: false, message }));

  it('speaks of litres for a measured product', () => {
    expect(parseQuantity('', 'Litre')).toEqual({ ok: false, message: 'Écrivez une quantité en litres, par exemple 1,5.' });
    expect(parseQuantity('0', 'Litre')).toEqual({ ok: false, message: 'La quantité doit être supérieure à 0.' });
    expect(parseQuantity('0,0625', 'Litre')).toEqual({ ok: false, message: 'Écrivez la quantité avec trois décimales au plus.' });
  });

  it('refuses an absurdly large weight', () => {
    expect(parseQuantity('9999999999', 'Kilogram').ok).toBe(false);
  });

  it('reads a piece count as a whole number only', () => {
    expect(parseQuantity('3', 'Piece')).toEqual({ ok: true, quantity: 3 });
    expect(parseQuantity('1,5', 'Piece')).toEqual({ ok: false, message: 'Écrivez un nombre entier de pièces.' });
  });
});

describe('lineTotal', () => {
  it.each([
    [800, 1250, 'Kilogram', 1000],
    [800, 333, 'Kilogram', 266], // 266,4
    [500, 1, 'Kilogram', 1], // 0,5 : demi vers le haut
    [700, 1429, 'Kilogram', 1000], // 1 000,3
    [1500, 2000, 'Litre', 3000],
    [600, 2, 'Piece', 1200],
    [0, 5000, 'Kilogram', 0],
  ] as const)('%i FCFA × %i (%s) = %i FCFA', (price, quantity, unit, expected) => expect(lineTotal(price, quantity, unit)).toBe(expected));

  it('takes a line without a unit for pieces, as the lines of before the weights', () => {
    expect(lineTotal(600, 2)).toBe(1200);
  });
});

describe('amountToQuantity: « pour 1 000 francs de riz »', () => {
  it('computes the weight and gives the real total', () => {
    expect(amountToQuantity(1000, 800)).toEqual({ ok: true, quantity: 1250, totalFcfa: 1000 });
  });

  it('rounds the weight to the gram and recomputes the total, which can differ by a few francs', () => {
    // 1 000 ÷ 700 = 1,42857 kg → 1,429 kg → 1 000,3 FCFA → 1 000 FCFA
    expect(amountToQuantity(1000, 700)).toEqual({ ok: true, quantity: 1429, totalFcfa: 1000 });
    // 1 000 ÷ 300 = 3,33333 kg → 3,333 kg → 999,9 → 1 000 FCFA
    expect(amountToQuantity(1000, 300)).toEqual({ ok: true, quantity: 3333, totalFcfa: 1000 });
    // 100 ÷ 900 = 0,11111 kg → 0,111 kg → 99,9 → 100 FCFA
    expect(amountToQuantity(100, 900)).toEqual({ ok: true, quantity: 111, totalFcfa: 100 });
  });

  it('refuses an amount that buys less than a gram', () => {
    expect(amountToQuantity(1, 5000)).toEqual({ ok: false, message: 'Ce montant est trop petit pour ce produit.' });
  });

  it('refuses a missing amount and a product without a price', () => {
    expect(amountToQuantity(0, 800)).toEqual({ ok: false, message: 'Écrivez un montant en FCFA.' });
    expect(amountToQuantity(1000, 0)).toEqual({ ok: false, message: 'Ce produit n’a pas de prix : impossible de calculer le poids.' });
  });
});

describe('quantityWords', () => {
  it('says the quantity and the total of a line in words', () => {
    expect(quantityWords(1250, 'Kilogram', 800)).toBe('1,250 kg · 1 000 FCFA');
    expect(quantityWords(2, 'Piece', 600)).toBe('2 · 1 200 FCFA');
  });
});

describe('parseThreshold, quantityInput, perUnit', () => {
  it('accepts 0 and an empty field as « no alert »', () => {
    expect(parseThreshold('0', 'Kilogram')).toEqual({ ok: true, quantity: 0 });
    expect(parseThreshold('', 'Piece')).toEqual({ ok: true, quantity: 0 });
  });

  it('reads decimals for a weighed product and integers for pieces', () => {
    expect(parseThreshold('2,5', 'Kilogram')).toEqual({ ok: true, quantity: 2500 });
    expect(parseThreshold('1,5', 'Piece').ok).toBe(false);
    expect(parseThreshold('1,2345', 'Litre').ok).toBe(false);
  });

  it('writes a quantity back the way it is typed', () => {
    expect(quantityInput(2500, 'Kilogram')).toBe('2,5');
    expect(quantityInput(12_000, 'Kilogram')).toBe('12');
    expect(quantityInput(250, 'Litre')).toBe('0,25');
    expect(quantityInput(7, 'Piece')).toBe('7');
  });

  it('names the unit next to a price', () => {
    expect(perUnit('Kilogram')).toBe(' par kg');
    expect(perUnit('Litre')).toBe(' par litre');
    expect(perUnit('Piece')).toBe('');
    expect(perUnit()).toBe('');
  });
});
