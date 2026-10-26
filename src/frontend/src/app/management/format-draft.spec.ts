import { FormatDraft, newDraft, validateFormats } from './format-draft';

const draft = (patch: Partial<FormatDraft> = {}): FormatDraft => ({
  ...newDraft(), label: '1 L', barcode: '', purchasePrice: '180', salePrice: '300', threshold: '3', ...patch,
});

describe('validateFormats', () => {
  it('turns the typed values into formats, trimming the text and reading the amounts', () => {
    const result = validateFormats([draft({ label: '  1,5   L ', barcode: ' 611 ', salePrice: '1 200' })]);

    expect(result.inputs).toEqual([
      { productId: expect.stringMatching(/^[0-9a-f-]{36}$/), label: '1,5 L', barcode: '611', purchasePriceFcfa: 180, salePriceFcfa: 1200, alertThreshold: 3, tracksStock: true, unit: 'Piece' },
    ]);
  });

  it('reads the threshold of a weighed product in the unit and keeps it in grams', () => {
    const result = validateFormats([draft({ unit: 'Kilogram', salePrice: '600', threshold: '2,5' })]);

    expect(result.inputs?.[0]).toMatchObject({ unit: 'Kilogram', alertThreshold: 2500, salePriceFcfa: 600 });
  });

  it('refuses a threshold with four decimals for a weighed product, and decimals for pieces', () => {
    expect(validateFormats([draft({ unit: 'Kilogram', threshold: '1,2345' })]).errors[0].threshold).toBeTruthy();
    expect(validateFormats([draft({ unit: 'Piece', threshold: '1,5' })]).errors[0].threshold).toContain('entiers');
  });

  it('lets a single format go without a label', () => {
    const result = validateFormats([draft({ label: '  ' })]);

    expect(result.inputs?.[0].label).toBeNull();
  });

  it('asks for a label on every format as soon as there are two', () => {
    const result = validateFormats([draft({ label: '1 L' }), draft({ label: '' })]);

    expect(result.inputs).toBeNull();
    expect(result.errors[1].label).toContain('Donnez un format');
    expect(result.errors[0].label).toBeUndefined();
  });

  it.each(['1 L', '1 l', '  1   L '])('refuses the same label written as « %s »', (second) => {
    const result = validateFormats([draft({ label: '1 L' }), draft({ label: second })]);

    expect(result.inputs).toBeNull();
    expect(result.errors[1].label).toContain('existe déjà');
  });

  it('explains each invalid number under its own field', () => {
    const result = validateFormats([draft({ salePrice: '12,5', purchasePrice: '-3', threshold: 'abc' })]);

    expect(result.inputs).toBeNull();
    expect(result.errors[0].salePrice).toContain('chiffres');
    expect(result.errors[0].purchasePrice).toContain('chiffres');
    expect(result.errors[0].threshold).toContain('entiers');
  });

  it('refuses the same barcode on two formats of the list', () => {
    const result = validateFormats([draft({ label: '1 L', barcode: '7' }), draft({ label: '2 L', barcode: '7' })]);

    expect(result.errors[1].barcode).toContain('déjà');
    expect(result.inputs).toBeNull();
  });

  it('keeps the product identifier of each draft, so a retry creates nothing twice', () => {
    const one = draft();
    const first = validateFormats([one]).inputs![0].productId;
    const second = validateFormats([one]).inputs![0].productId;

    expect(first).toBe(second);
    expect(first).toBe(one.productId);
  });

  it('asks for at least one format', () => {
    expect(validateFormats([]).inputs).toBeNull();
  });
});
