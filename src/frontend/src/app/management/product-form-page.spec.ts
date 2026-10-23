import { ApiError } from '../sync/sync-api';
import { ProductFormPage } from './product-form-page';
import { fakeApi, product, render } from './test-support';

async function fill(page: Awaited<ReturnType<typeof render>>) {
  await page.type('#nom', ' Jus 1L ');
  await page.type('#code-barres', ' 6111000000011 ');
  await page.type('#categorie', 'Boissons');
  await page.type('#prix-vente', '600');
  await page.type('#prix-achat', '400');
  await page.type('#seuil', '5');
}

describe('ProductFormPage: creation', () => {
  it('creates the product with trimmed values, goes back to the list and confirms (SC-G1)', async () => {
    const api = fakeApi();
    const page = await render(ProductFormPage, { api });
    await fill(page);

    await page.submit();

    expect(api.create).toHaveBeenCalledTimes(1);
    const [id, input] = api.create.mock.calls[0];
    expect(id).toMatch(/^[0-9a-f-]{36}$/);
    expect(input).toEqual({
      name: 'Jus 1L', barcode: '6111000000011', category: 'Boissons', purchasePriceFcfa: 400, salePriceFcfa: 600, alertThreshold: 5, tracksStock: true,
    });
    expect(page.navigate).toHaveBeenCalledWith('/gestion/produits');
    expect(page.flash.current()).toBe('Produit créé.');
  });

  it('explains each invalid field under the field and creates nothing', async () => {
    const api = fakeApi();
    const page = await render(ProductFormPage, { api });
    await page.type('#prix-vente', '12,5');
    await page.type('#prix-achat', '-3');
    await page.type('#seuil', 'abc');

    await page.submit();

    expect(api.create).not.toHaveBeenCalled();
    expect(page.root.querySelector('#nom-erreur')?.textContent).toContain('obligatoire');
    expect(page.root.querySelector('#prix-vente-erreur')?.textContent).toContain('chiffres');
    expect(page.root.querySelector('#prix-achat-erreur')?.textContent).toContain('chiffres');
    expect(page.root.querySelector('#seuil-erreur')?.textContent).toContain('entiers');
  });

  it('shows a barcode already used under the barcode field and keeps the form (SC-G2)', async () => {
    const api = fakeApi();
    api.create.mockRejectedValue(new ApiError(409, 'barcode_taken', 'Ce code-barres est déjà utilisé par un autre produit.'));
    const page = await render(ProductFormPage, { api });
    await fill(page);

    await page.submit();

    expect(page.root.querySelector('#code-erreur')?.textContent).toContain('déjà utilisé');
    expect(page.navigate).not.toHaveBeenCalled();
    expect(page.root.querySelector<HTMLInputElement>('#nom')!.value).toBe(' Jus 1L ');
  });

  it('keeps the same product id on a retry after a network failure, so nothing is created twice (SC-G6)', async () => {
    const api = fakeApi();
    api.create.mockRejectedValueOnce(new ApiError(0, 'network', ''));
    const page = await render(ProductFormPage, { api });
    await fill(page);

    await page.submit();
    expect(page.root.querySelector('[role="alert"]')?.textContent).toContain('injoignable');
    await page.submit();

    expect(api.create).toHaveBeenCalledTimes(2);
    expect(api.create.mock.calls[1][0]).toBe(api.create.mock.calls[0][0]);
    expect(page.navigate).toHaveBeenCalledWith('/gestion/produits');
  });

  it('lets the user create a service without stock tracking', async () => {
    const api = fakeApi();
    const page = await render(ProductFormPage, { api });
    await fill(page);
    const box = page.root.querySelector<HTMLInputElement>('input[type="checkbox"]')!;
    box.checked = false;
    box.dispatchEvent(new Event('change'));
    await page.settle();

    await page.submit();

    expect(api.create.mock.calls[0][1].tracksStock).toBe(false);
  });
});

describe('ProductFormPage: edition', () => {
  it('loads the product, shows its values, and fixes the stock tracking choice', async () => {
    const api = fakeApi();
    api.list.mockResolvedValue([product({ id: 'p-9', name: 'Savon', salePriceFcfa: 250, purchasePriceFcfa: 150, alertThreshold: 3 })]);
    const page = await render(ProductFormPage, { api, params: { id: 'p-9' } });

    expect(page.root.querySelector<HTMLInputElement>('#nom')!.value).toBe('Savon');
    expect(page.root.querySelector<HTMLInputElement>('#prix-vente')!.value).toBe('250');
    expect(page.root.querySelector<HTMLInputElement>('input[type="checkbox"]')!.disabled).toBe(true);
    expect(page.has('Modifier le produit')).toBe(true);
  });

  it('updates the product on its own id, without the stock tracking field', async () => {
    const api = fakeApi();
    api.list.mockResolvedValue([product({ id: 'p-9' })]);
    const page = await render(ProductFormPage, { api, params: { id: 'p-9' } });
    await page.type('#prix-vente', '700');

    await page.submit();

    expect(api.update).toHaveBeenCalledWith('p-9', {
      name: "Jus d'orange 1L", barcode: '111', category: 'Boissons', purchasePriceFcfa: 400, salePriceFcfa: 700, alertThreshold: 5,
    });
    expect(page.flash.current()).toBe('Produit modifié.');
  });

  it('says so when the product does not exist', async () => {
    const api = fakeApi();
    api.list.mockResolvedValue([]);
    const page = await render(ProductFormPage, { api, params: { id: 'inconnu' } });
    expect(page.root.querySelector('[role="alert"]')?.textContent).toContain('Produit introuvable');
  });

  it('asks for a confirmation before deactivating, then deactivates and confirms (SC-G1)', async () => {
    const api = fakeApi();
    api.list.mockResolvedValue([product({ id: 'p-9' })]);
    const page = await render(ProductFormPage, { api, params: { id: 'p-9' } });

    await page.click('Désactiver ce produit');
    expect(page.has('Désactiver « Jus d\'orange 1L » ?')).toBe(true);
    expect(api.deactivate).not.toHaveBeenCalled();

    await page.click('Non, le garder');
    expect(api.deactivate).not.toHaveBeenCalled();

    await page.click('Désactiver ce produit');
    await page.click('Oui, désactiver');

    expect(api.deactivate).toHaveBeenCalledWith('p-9');
    expect(page.flash.current()).toBe('Produit désactivé.');
    expect(page.navigate).toHaveBeenCalledWith('/gestion/produits');
  });

  it('does not offer deactivation for a product that is already deactivated', async () => {
    const api = fakeApi();
    api.list.mockResolvedValue([product({ id: 'p-9', isActive: false })]);
    const page = await render(ProductFormPage, { api, params: { id: 'p-9' } });
    expect(page.has('Désactiver ce produit')).toBe(false);
  });
});
