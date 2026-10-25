import { ApiError } from '../sync/sync-api';
import { LabelsPage } from './labels-page';
import { ManagedProduct } from './products-api';
import { fakeApi, product as baseProduct, render } from './test-support';

const item = (overrides: Partial<ManagedProduct> = {}): ManagedProduct => baseProduct({ id: 'p-1', name: 'Pain de mie', barcode: '2900000000018', salePriceFcfa: 500, ...overrides });

async function open(products: ManagedProduct[], options: { role?: string; online?: boolean } = {}) {
  const api = fakeApi();
  api.list.mockResolvedValue(products);
  const page = await render(LabelsPage, { api, role: options.role ?? 'Manager', online: options.online });
  const frame = () => page.root.querySelector<HTMLIFrameElement>('iframe')!;
  const text = () => page.root.textContent ?? '';
  return { page, api, frame, text };
}

describe('LabelsPage', () => {
  it('lists the active products with their price and their code, or the lack of one', async () => {
    const { text } = await open([item(), item({ id: 'p-2', name: 'Riz', formatLabel: '25 kg', barcode: null, salePriceFcfa: 17500 })]);

    expect(text()).toContain('Pain de mie');
    expect(text()).toContain('2900000000018');
    expect(text()).toContain('Riz');
    expect(text()).toContain('25 kg');
    expect(text()).toContain('Pas de code-barres');
    expect(text()).toContain('17 500 FCFA');
  });

  it('says what to do when there is no product', async () => {
    const { text } = await open([]);
    expect(text()).toContain('Aucun produit');
  });

  it('filters by name and shows only the products without a code on demand', async () => {
    const { page, text } = await open([item(), item({ id: 'p-2', name: 'Riz', barcode: null })]);

    await page.type('#recherche-produit', 'riz');
    expect(text()).not.toContain('Pain de mie');
    expect(text()).toContain('Riz');

    await page.type('#recherche-produit', '');
    await page.check('#sans-code', true);
    expect(text()).not.toContain('Pain de mie');
    expect(text()).toContain('Riz');
  });

  it('selects every listed product at once and counts the labels', async () => {
    const { page, text } = await open([item(), item({ id: 'p-2', name: 'Jus', barcode: '6111000000014' })]);

    await page.click('Tout sélectionner');

    expect(text()).toContain('2 étiquettes');
    expect(text()).toContain('1 planche');
  });

  it('counts the copies of each product and keeps them between 1 and 99', async () => {
    const { page, text } = await open([item()]);
    await page.check('[aria-label="Choisir Pain de mie"]', true);

    await page.type('[aria-label="Nombre d’étiquettes pour Pain de mie"]', '30');
    expect(text()).toContain('30 étiquettes');
    expect(text()).toContain('2 planches');

    await page.type('[aria-label="Nombre d’étiquettes pour Pain de mie"]', '500');
    expect(text()).toContain('99 étiquettes');
  });

  it('refuses to print while a chosen product has no code, and says how many', async () => {
    const { page, text } = await open([item({ id: 'a', name: 'Riz', barcode: null }), item({ id: 'b', name: 'Pain', barcode: null })]);
    await page.click('Tout sélectionner');

    expect(text()).toContain('2 produits n’ont pas de code-barres');
    expect(text()).toContain('Créer un code interne');
    expect([...page.root.querySelectorAll('button')].find((button) => button.textContent?.includes('Imprimer'))!.disabled).toBe(true);
  });

  it('creates an internal code on the spot, then allows the print', async () => {
    const { page, api, text } = await open([item({ id: 'a', name: 'Riz', barcode: null })]);
    await page.click('Tout sélectionner');

    await page.click('Créer un code interne pour Riz');

    expect(api.createInternalBarcode).toHaveBeenCalledWith('a');
    expect(text()).toContain('2900000000018');
    expect(text()).toContain('Code interne créé');
    expect(text()).not.toContain('n’ont pas de code-barres');
    expect([...page.root.querySelectorAll('button')].find((button) => button.textContent?.includes('Imprimer'))!.disabled).toBe(false);
  });

  it('shows the refusal of the server in French and keeps the product without a code', async () => {
    const { page, api, text } = await open([item({ id: 'a', name: 'Riz', barcode: null })]);
    api.createInternalBarcode.mockRejectedValue(new ApiError(422, 'rule_violation', 'Ce produit a déjà un code-barres : réimprimez-le au lieu d’en créer un autre.'));
    await page.click('Tout sélectionner');

    await page.click('Créer un code interne pour Riz');

    expect(page.root.querySelector('[role="alert"]')?.textContent).toContain('réimprimez-le');
    expect(text()).toContain('Pas de code-barres');
  });

  it('does not offer to create a code to a stock keeper, who asks a manager', async () => {
    const { page, text } = await open([item({ id: 'a', name: 'Riz', barcode: null })], { role: 'StockKeeper' });
    await page.click('Tout sélectionner');

    expect(page.root.querySelector('[aria-label="Créer un code interne pour Riz"]')).toBeNull();
    expect(text()).toContain('Demandez à un gérant');
  });

  it('shows what will be printed in a preview made of the same page as the print', async () => {
    const { page, frame } = await open([item({ name: 'Pain de mie' })]);
    await page.click('Tout sélectionner');

    expect(frame().srcdoc).toContain('Pain de mie');
    expect(frame().srcdoc).toContain('@page { size: 210mm 297mm');
    expect(frame().srcdoc).toContain('<svg ');
  });

  it('switches to the roll and says there is one label per page', async () => {
    const { page, frame, text } = await open([item()]);
    await page.click('Tout sélectionner');

    await page.select('#format', 'roll');

    expect(text()).toContain('1 étiquette');
    expect(text()).toContain('1 par page');
    expect(frame().srcdoc).toContain('@page { size: 50mm 30mm');
    expect(page.root.querySelector('#premiere')).toBeNull();
  });

  it('starts the sheet at the label asked', async () => {
    const { page, frame } = await open([item()]);
    await page.click('Tout sélectionner');

    await page.type('#premiere', '20');

    expect(frame().srcdoc.match(/class="label empty"/g)).toHaveLength(23);
  });

  it('opens the print dialog of the device on the preview', async () => {
    const { page, frame } = await open([item()]);
    await page.click('Tout sélectionner');
    const print = vi.fn();
    frame().contentWindow!.print = print;

    await page.click('Imprimer');

    expect(print).toHaveBeenCalledOnce();
  });

  it('says nothing is chosen and keeps the print disabled', async () => {
    const { page, text } = await open([item()]);

    expect(text()).toContain('Aucune étiquette choisie');
    expect([...page.root.querySelectorAll('button')].find((button) => button.textContent?.includes('Imprimer'))!.disabled).toBe(true);
  });

  it('says it needs the network when offline', async () => {
    const { text } = await open([item()], { online: false });
    expect(text()).toContain('hors ligne');
  });

  it('shows a loading error with a retry', async () => {
    const api = fakeApi();
    api.list.mockRejectedValueOnce(new ApiError(0, 'network', ''));
    const page = await render(LabelsPage, { api, role: 'Manager' });
    expect(page.root.querySelector('[role="alert"]')?.textContent).toContain('injoignable');

    api.list.mockResolvedValue([item()]);
    await page.click('Réessayer');
    expect(page.has('Pain de mie')).toBe(true);
  });
});
