import { ApiError } from '../sync/sync-api';
import { ProductsPage } from './products-page';
import { fakeApi, product, render } from './test-support';
import { TestBed } from '@angular/core/testing';

describe('ProductsPage', () => {
  it('lists the products with their prices, stock and state in words', async () => {
    const api = fakeApi();
    api.list.mockResolvedValue([
      product(),
      product({ id: 'p-2', name: 'Savon', quantityOnHand: 3, isLowStock: true }),
      product({ id: 'p-3', name: 'Eau', quantityOnHand: 0, isLowStock: true }),
      product({ id: 'p-4', name: 'Coupe', tracksStock: false, quantityOnHand: 0 }),
    ]);
    const page = await render(ProductsPage, { api });

    expect(page.has('Stock correct')).toBe(true);
    expect(page.has('Stock bas')).toBe(true);
    expect(page.has('Rupture de stock')).toBe(true);
    expect(page.has('Sans suivi de stock')).toBe(true);
    expect(page.has('Prix d’achat')).toBe(true);
    expect(page.root.querySelectorAll('tbody tr')).toHaveLength(4);
  });

  it('offers creation and edition to a manager, but only stock actions to a stock keeper (SC-G5)', async () => {
    const api = fakeApi();
    api.list.mockResolvedValue([product()]);

    const manager = await render(ProductsPage, { api, role: 'Manager' });
    expect(manager.has('Nouveau produit')).toBe(true);
    expect(manager.root.querySelector('[aria-label="Modifier Jus d\'orange 1L"]')).toBeTruthy();
    expect(manager.has('Afficher les produits désactivés')).toBe(true);

    TestBed.resetTestingModule();
    const keeper = await render(ProductsPage, { api, role: 'StockKeeper' });
    expect(keeper.has('Nouveau produit')).toBe(false);
    expect(keeper.root.querySelector('[aria-label^="Modifier"]')).toBeNull();
    expect(keeper.root.querySelector('[aria-label^="Stock de"]')).toBeTruthy();
    expect(keeper.has('Afficher les produits désactivés')).toBe(false);
  });

  it('keeps the scrolling table inside a positioned container so hidden text cannot widen the page', async () => {
    const api = fakeApi();
    api.list.mockResolvedValue([product()]);
    const page = await render(ProductsPage, { api });
    // Régression vue à 320 px : sans « relative », le texte sr-only de l'en-tête sortait du conteneur et élargissait la page.
    expect(page.root.querySelector('table')!.closest('div')!.classList.contains('relative')).toBe(true);
  });

  it('hides the purchase price column when the server did not send it', async () => {
    const api = fakeApi();
    api.list.mockResolvedValue([product({ purchasePriceFcfa: null })]);
    const page = await render(ProductsPage, { api });
    expect(page.has('Prix d’achat')).toBe(false);
  });

  it('searches with the typed term and says when nothing matches', async () => {
    const api = fakeApi();
    api.list.mockResolvedValue([product()]);
    const page = await render(ProductsPage, { api });

    api.list.mockResolvedValue([]);
    await page.type('#recherche-produit', ' xyz ');
    await page.submit();

    expect(api.list).toHaveBeenLastCalledWith({ search: 'xyz', includeInactive: false });
    expect(page.has('Aucun produit ne correspond à « xyz »')).toBe(true);
  });

  it('switches to the low stock list, most urgent first, and explains an empty one', async () => {
    const api = fakeApi();
    api.list.mockResolvedValue([product()]);
    api.lowStock.mockResolvedValue([]);
    const page = await render(ProductsPage, { api });

    await page.check('input[type="checkbox"]', true);

    expect(api.lowStock).toHaveBeenCalled();
    expect(page.has('le stock est correct')).toBe(true);
  });

  it('asks the server for the deactivated products when the box is ticked', async () => {
    const api = fakeApi();
    api.list.mockResolvedValue([product()]);
    const page = await render(ProductsPage, { api });

    await page.check('input[type="checkbox"]:not(:first-of-type), label:nth-of-type(2) input', true);

    expect(api.list).toHaveBeenLastCalledWith({ search: '', includeInactive: true });
  });

  it('shows the confirmation of the previous screen once', async () => {
    const api = fakeApi();
    api.list.mockResolvedValue([product()]);
    const page = await render(ProductsPage, { api, flash: 'Produit créé.' });
    expect(page.root.querySelector('[role="status"]')?.textContent).toContain('Produit créé.');
    expect(page.flash.current()).toBe('');
  });

  it('explains a server failure and lets the user retry', async () => {
    const api = fakeApi();
    api.list.mockRejectedValueOnce(new ApiError(0, 'network', ''));
    const page = await render(ProductsPage, { api });
    expect(page.root.querySelector('[role="alert"]')?.textContent).toContain('injoignable');

    api.list.mockResolvedValue([product()]);
    await page.click('Réessayer');
    expect(page.root.querySelectorAll('tbody tr')).toHaveLength(1);
  });

  it('says when it is offline, without hiding the screen (SC-G7)', async () => {
    const api = fakeApi();
    api.list.mockResolvedValue([]);
    const page = await render(ProductsPage, { api, online: false });
    expect(page.root.querySelector('[role="note"]')?.textContent).toContain('hors ligne');
  });

  describe('families', () => {
    const eau = (id: string, label: string, price: number, extra = {}) =>
      product({ id, name: `Eau minérale · ${label}`, familyId: 'eau', familyName: 'Eau minérale', formatLabel: label, salePriceFcfa: price, category: 'Boissons', ...extra });

    it('shows a family once, with a header and one row per format from the cheapest', async () => {
      const api = fakeApi();
      api.list.mockResolvedValue([eau('a', '1,5 L', 400), eau('b', '30 cl', 150), eau('c', '1 L', 300), product({ id: 's', name: 'Sel fin' })]);
      const page = await render(ProductsPage, { api });

      expect(page.root.querySelectorAll('tbody')).toHaveLength(2);
      const rows = [...page.root.querySelectorAll('tbody:first-of-type tr')].map((row) => row.textContent ?? '');
      expect(rows[0]).toContain('Eau minérale');
      expect(rows[0]).toContain('3 formats');
      expect(rows[1]).toContain('30 cl');
      expect(rows[3]).toContain('1,5 L');
    });

    it('lets a manager open the family, but not a stock keeper', async () => {
      const api = fakeApi();
      api.list.mockResolvedValue([eau('a', '1 L', 300), eau('b', '2 L', 500)]);
      const manager = await render(ProductsPage, { api, role: 'Manager' });
      const link = manager.root.querySelector('[aria-label="Famille Eau minérale"]');
      expect(link?.getAttribute('href')).toBe('/gestion/produits/famille/eau');
      expect(manager.has('Regrouper dans une famille')).toBe(true);

      TestBed.resetTestingModule();
      const keeper = await render(ProductsPage, { api, role: 'StockKeeper' });
      expect(keeper.root.querySelector('[aria-label^="Famille"]')).toBeNull();
      expect(keeper.has('Regrouper dans une famille')).toBe(false);
      expect(keeper.root.querySelector('[aria-label="Stock de Eau minérale · 1 L"]')).toBeTruthy();
    });

    it('gives a single product a way to add formats', async () => {
      const api = fakeApi();
      api.list.mockResolvedValue([product({ id: 'sel', name: 'Sel fin', familyId: 'sel', familyName: 'Sel fin' })]);
      const page = await render(ProductsPage, { api });

      expect(page.root.querySelector('[aria-label="Formats de Sel fin"]')?.getAttribute('href')).toBe('/gestion/produits/famille/sel');
      expect(page.root.querySelectorAll('tbody')).toHaveLength(1);
    });
  });
});
