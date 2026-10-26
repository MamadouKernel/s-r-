import { ManagedProduct } from './products-api';
import { blockedProducts, clampCopies, clampStart, labelOf, LABEL_FORMATS, labelsDocument, planLabels } from './labels';

const product = (overrides: Partial<ManagedProduct> = {}): ManagedProduct => ({
  id: 'p-1', name: 'Pain de mie', barcode: '2900000000018', category: null, purchasePriceFcfa: 300, salePriceFcfa: 500, alertThreshold: 0,
  tracksStock: false, isActive: true, quantityOnHand: 0, isLowStock: false, ...overrides,
});

const pick = (items: [ManagedProduct, number][]) => items.map(([item, copies]) => ({ product: item, copies }));

describe('labelOf', () => {
  it('gives the name, the price in FCFA and the drawn code', () => {
    const label = labelOf(product({ name: 'Pain de mie', salePriceFcfa: 1500 }));

    expect(label.title).toBe('Pain de mie');
    expect(label.subtitle).toBeNull();
    expect(label.price).toBe('1 500 FCFA');
    expect(label.symbol?.kind).toBe('ean13');
    expect(label.code).toBe('2900000000018');
  });

  it('puts the format on a second line', () => {
    expect(labelOf(product({ name: 'Riz parfumé', formatLabel: '25 kg' })).subtitle).toBe('25 kg');
  });

  it('draws a short or alphanumeric code in Code 128', () => {
    expect(labelOf(product({ barcode: 'RIZ-25' })).symbol?.kind).toBe('code128');
  });

  it('says the code cannot be printed rather than drawing it wrong', () => {
    const label = labelOf(product({ barcode: 'Riz é' }));

    expect(label.symbol).toBeNull();
    expect(label.code).toBe('Riz é');
  });
});

describe('blockedProducts', () => {
  it('lists the chosen products that have no barcode', () => {
    const blocked = blockedProducts(pick([[product({ id: 'a' }), 1], [product({ id: 'b', barcode: null }), 2], [product({ id: 'c', barcode: '  ' }), 1]]));

    expect(blocked.map((item) => item.id)).toEqual(['b', 'c']);
  });

  it('ignores a product with no copy', () => {
    expect(blockedProducts(pick([[product({ barcode: null }), 0]]))).toEqual([]);
  });
});

describe('clamps', () => {
  it('keeps the copies between 1 and 99', () => {
    expect([0, 1, 5, 99, 100, -3, Number.NaN].map(clampCopies)).toEqual([1, 1, 5, 99, 99, 1, 1]);
  });

  it('keeps the first label of a sheet between 1 and its size', () => {
    expect([0, 1, 20, 24, 25, -2, Number.NaN].map((value) => clampStart(value, 24))).toEqual([1, 1, 20, 24, 24, 1, 1]);
  });
});

describe('planLabels on an A4 sheet', () => {
  const sheet = LABEL_FORMATS.a4;

  it('is 24 labels, 3 columns by 8 rows', () => {
    expect([sheet.columns, sheet.rows, sheet.columns * sheet.rows]).toEqual([3, 8, 24]);
  });

  it('puts 3 products of 2, 1 and 5 copies in order on a single sheet, the other cells empty', () => {
    const pages = planLabels(pick([[product({ id: 'a', name: 'A' }), 2], [product({ id: 'b', name: 'B' }), 1], [product({ id: 'c', name: 'C' }), 5]]), 'a4', 1);

    expect(pages).toHaveLength(1);
    expect(pages[0]).toHaveLength(24);
    expect(pages[0].slice(0, 8).map((cell) => cell?.title)).toEqual(['A', 'A', 'B', 'C', 'C', 'C', 'C', 'C']);
    expect(pages[0].slice(8).every((cell) => cell === null)).toBe(true);
  });

  it('starts at the label asked: the same 8 labels from n° 20 fill the end of the sheet then the start of the next', () => {
    const pages = planLabels(pick([[product({ id: 'a', name: 'A' }), 2], [product({ id: 'b', name: 'B' }), 1], [product({ id: 'c', name: 'C' }), 5]]), 'a4', 20);

    expect(pages).toHaveLength(2);
    expect(pages[0].slice(0, 19).every((cell) => cell === null)).toBe(true);
    expect(pages[0].slice(19).map((cell) => cell?.title)).toEqual(['A', 'A', 'B', 'C', 'C']);
    expect(pages[1].slice(0, 3).map((cell) => cell?.title)).toEqual(['C', 'C', 'C']);
    expect(pages[1].slice(3).every((cell) => cell === null)).toBe(true);
  });

  it('opens a second sheet after 24 labels', () => {
    const pages = planLabels(pick([[product(), 30]]), 'a4', 1);

    expect(pages.map((page) => page.filter(Boolean).length)).toEqual([24, 6]);
  });

  it('skips the products with no copy', () => {
    expect(planLabels(pick([[product(), 0]]), 'a4', 1)).toEqual([]);
  });

  it('counts a start beyond the sheet as the last cell', () => {
    const pages = planLabels(pick([[product(), 1]]), 'a4', 99);

    expect(pages[0].findIndex(Boolean)).toBe(23);
  });
});

describe('planLabels on a roll', () => {
  it('is one label per page of 50 × 30 mm', () => {
    expect([LABEL_FORMATS.roll.widthMm, LABEL_FORMATS.roll.heightMm]).toEqual([50, 30]);

    const pages = planLabels(pick([[product({ name: 'A' }), 2], [product({ name: 'B' }), 1]]), 'roll', 1);

    expect(pages.map((page) => page.map((cell) => cell?.title))).toEqual([['A'], ['A'], ['B']]);
  });

  it('ignores the start position', () => {
    expect(planLabels(pick([[product(), 2]]), 'roll', 20)).toHaveLength(2);
  });
});

describe('labelsDocument', () => {
  const doc = (items: [ManagedProduct, number][], format: 'a4' | 'roll' = 'a4', start = 1) => labelsDocument(planLabels(pick(items), format, start), format);

  it('is a whole page with the paper size for the print dialog: A4 for a sheet', () => {
    const html = doc([[product(), 1]]);

    expect(html.startsWith('<!doctype html>')).toBe(true);
    expect(html).toContain('lang="fr"');
    expect(html).toContain('@page { size: 210mm 297mm; margin: 0; }');
    expect(html).toContain('grid-template-columns: repeat(3, 70mm)');
    expect(html).toContain('grid-template-rows: repeat(8, 37mm)');
  });

  it('is 50 × 30 mm pages for a roll', () => {
    const html = doc([[product(), 1]], 'roll');

    expect(html).toContain('@page { size: 50mm 30mm; margin: 0; }');
    expect(html.match(/class="page"/g)).toHaveLength(1);
  });

  it('writes the name, the format, the price, the code and the bars of every label, and leaves the skipped cells empty', () => {
    const html = doc([[product({ name: 'Riz parfumé', formatLabel: '25 kg', salePriceFcfa: 17500 }), 2]], 'a4', 23);

    expect(html.match(/class="label"/g)).toHaveLength(2);
    expect(html.match(/class="label empty"/g)).toHaveLength(22);
    expect(html.match(/<svg /g)).toHaveLength(2);
    expect(html).toContain('Riz parfumé');
    expect(html).toContain('25 kg');
    expect(html).toContain('17\u00a0500\u00a0FCFA');
    expect(html).toContain('2900000000018');
  });

  it('opens a page per sheet', () => {
    expect(doc([[product(), 30]]).match(/class="page"/g)).toHaveLength(2);
  });

  it('escapes what comes from a product so a name can never inject markup', () => {
    const html = doc([[product({ name: '<script>alert(1)</script> & "x"' }), 1]]);

    expect(html).not.toContain('<script>alert');
    expect(html).toContain('&lt;script&gt;alert(1)&lt;/script&gt; &amp; &quot;x&quot;');
  });

  it('says « Code non imprimable » instead of drawing a code that no barcode can carry', () => {
    const html = doc([[product({ barcode: 'Riz é' }), 1]]);

    expect(html).toContain('Code non imprimable');
    expect(html).not.toContain('<svg ');
  });
});
