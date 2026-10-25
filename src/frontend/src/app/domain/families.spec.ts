import { CatalogueProduct } from './types';
import { groupByFamily, initialsOf, matchesSearch } from './families';

const base = { barcode: null, category: null, tracksStock: true, quantityOnHand: 5 };

function product(id: string, name: string, price: number, extra: Partial<CatalogueProduct> = {}): CatalogueProduct {
  return { ...base, id, name, salePriceFcfa: price, ...extra };
}

describe('groupByFamily', () => {
  it('gathers the formats of a family and sorts them from the cheapest', () => {
    const groups = groupByFamily([
      product('a', 'Eau minérale · 1,5 L', 400, { familyId: 'eau', familyName: 'Eau minérale', formatLabel: '1,5 L' }),
      product('b', 'Eau minérale · 30 cl', 150, { familyId: 'eau', familyName: 'Eau minérale', formatLabel: '30 cl' }),
      product('c', 'Eau minérale · 1 L', 300, { familyId: 'eau', familyName: 'Eau minérale', formatLabel: '1 L' }),
    ]);

    expect(groups).toHaveLength(1);
    expect(groups[0].name).toBe('Eau minérale');
    expect(groups[0].formats.map((format) => format.formatLabel)).toEqual(['30 cl', '1 L', '1,5 L']);
  });

  it('treats a product without family as its own family', () => {
    const groups = groupByFamily([product('a', 'Savon', 200), product('b', 'Sel fin', 100)]);

    expect(groups.map((group) => group.name)).toEqual(['Savon', 'Sel fin']);
    expect(groups.every((group) => group.formats.length === 1)).toBe(true);
  });

  it('keeps the order in which families first appear', () => {
    const groups = groupByFamily([
      product('a', 'Eau · 1 L', 300, { familyId: 'eau', familyName: 'Eau', formatLabel: '1 L' }),
      product('b', 'Riz', 500),
      product('c', 'Eau · 2 L', 500, { familyId: 'eau', familyName: 'Eau', formatLabel: '2 L' }),
    ]);

    expect(groups.map((group) => group.name)).toEqual(['Eau', 'Riz']);
  });

  it('takes the photo of the first format that has one, and none when no format has one', () => {
    const withPhoto = groupByFamily([
      product('a', 'Eau · 1 L', 300, { familyId: 'eau', familyName: 'Eau', formatLabel: '1 L' }),
      product('b', 'Eau · 2 L', 500, { familyId: 'eau', familyName: 'Eau', formatLabel: '2 L', photoId: 'p1' }),
    ]);
    const without = groupByFamily([product('a', 'Savon', 200)]);

    expect(withPhoto[0].photoId).toBe('p1');
    expect(without[0].photoId).toBeNull();
  });

  it('shows the format name alone when a search leaves a single format of the family', () => {
    const [group] = groupByFamily([product('a', 'Riz parfumé · 25 kg', 14500, { familyId: 'riz', familyName: 'Riz parfumé', formatLabel: '25 kg' })]);

    expect(group.title).toBe('Riz parfumé · 25 kg');
    expect(group.formats).toHaveLength(1);
  });

  it('shows the family name when several formats are left', () => {
    const [group] = groupByFamily([
      product('a', 'Riz · 5 kg', 3200, { familyId: 'riz', familyName: 'Riz', formatLabel: '5 kg' }),
      product('b', 'Riz · 25 kg', 14500, { familyId: 'riz', familyName: 'Riz', formatLabel: '25 kg' }),
    ]);

    expect(group.title).toBe('Riz');
  });
});

describe('matchesSearch', () => {
  const rice = product('a', 'Riz parfumé · 25 kg', 14500, { barcode: '611123' });

  it.each([
    ['riz', true],
    ['RIZ parfumé', true],
    ['riz 25', true],
    ['25 riz', true],
    ['parfume', true],
    ['riz 50', false],
    ['eau', false],
  ])('matches %s -> %s', (term, expected) => expect(matchesSearch(rice, term)).toBe(expected));

  it('matches an exact barcode and an empty search matches everything', () => {
    expect(matchesSearch(rice, '611123')).toBe(true);
    expect(matchesSearch(rice, '   ')).toBe(true);
  });
});

describe('initialsOf', () => {
  it.each([
    ['Eau minérale', 'EM'],
    ['Riz parfumé · 25 kg', 'RP'],
    ['Savon', 'SA'],
    ['  ', '?'],
    ['élan', 'ÉL'],
  ])('%s -> %s', (name, expected) => expect(initialsOf(name)).toBe(expected));
});
