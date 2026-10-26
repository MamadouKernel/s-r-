import { ImageService } from '../core/image';
import { ApiError } from '../sync/sync-api';
import { ProductFormPage } from './product-form-page';
import { fakeApi, product, render, scanInto } from './test-support';

describe('ProductFormPage: edition', () => {
  it('loads the product, shows its values, and fixes the stock tracking choice', async () => {
    const api = fakeApi();
    api.list.mockResolvedValue([product({ id: 'p-9', name: 'Savon', salePriceFcfa: 250, purchasePriceFcfa: 150, alertThreshold: 3 })]);
    const page = await render(ProductFormPage, { api, params: { id: 'p-9' } });

    expect(page.root.querySelector<HTMLInputElement>('#nom')!.value).toBe('Savon');
    expect(page.root.querySelector<HTMLInputElement>('#prix-vente')!.value).toBe('250');
    expect(page.root.querySelector<HTMLInputElement>('input[type="checkbox"]')!.disabled).toBe(true);
    expect(page.has('Modifier le produit')).toBe(true);
    expect(page.has('Nouveau produit')).toBe(false);
  });

  it('updates the product on its own id, without the stock tracking field', async () => {
    const api = fakeApi();
    api.list.mockResolvedValue([product({ id: 'p-9' })]);
    const page = await render(ProductFormPage, { api, params: { id: 'p-9' } });
    await page.type('#prix-vente', '700');

    await page.submit();

    expect(api.update).toHaveBeenCalledWith('p-9', {
      name: "Jus d'orange 1L", barcode: '111', category: 'Boissons', purchasePriceFcfa: 400, salePriceFcfa: 700, alertThreshold: 5, formatLabel: null, unit: 'Piece',
    });
    expect(page.flash.current()).toBe('Produit modifié.');
  });

  it('shows the unit of a weighed product as fixed, with the reason, and the threshold in kilos', async () => {
    const api = fakeApi();
    api.list.mockResolvedValue([product({ id: 'p-9', unit: 'Kilogram', salePriceFcfa: 600, alertThreshold: 2_500, quantityOnHand: 12_000 })]);
    const page = await render(ProductFormPage, { api, params: { id: 'p-9' } });

    expect(page.root.querySelector<HTMLSelectElement>('#unite')!.disabled).toBe(true);
    expect(page.has('Au poids (kg)')).toBe(true);
    expect(page.has('Cette unité ne peut plus changer')).toBe(true);
    expect(page.has('Prix de vente (FCFA par kg)')).toBe(true);
    expect(page.root.querySelector<HTMLInputElement>('#seuil')!.value).toBe('2,5');

    await page.type('#seuil', '3');
    await page.submit();

    expect(api.update).toHaveBeenCalledWith('p-9', expect.objectContaining({ alertThreshold: 3_000, unit: 'Kilogram' }));
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

describe('ProductFormPage: formats', () => {
  const eau = (overrides = {}) => product({ id: 'w1', name: 'Eau minérale · 1 L', familyId: 'eau', familyName: 'Eau minérale', formatLabel: '1 L', ...overrides });
  const sibling = product({ id: 'w15', name: 'Eau minérale · 1,5 L', familyId: 'eau', familyName: 'Eau minérale', formatLabel: '1,5 L' });

  it('shows the family name and the format apart, and sends the new format label', async () => {
    const api = fakeApi();
    api.list.mockResolvedValue([eau(), sibling]);
    const page = await render(ProductFormPage, { api, params: { id: 'w1' } });
    expect(page.root.querySelector<HTMLInputElement>('#nom')!.value).toBe('Eau minérale');
    expect(page.root.querySelector<HTMLInputElement>('#format')!.value).toBe('1 L');
    expect(page.has('Ce nom est celui de toute la famille (2 formats)')).toBe(true);

    await page.type('#format', '  1   litre ');
    await page.submit();

    expect(api.update.mock.calls[0][1]).toMatchObject({ name: 'Eau minérale', formatLabel: '1 litre' });
  });

  it('asks for a format when the family has several, and sends nothing', async () => {
    const api = fakeApi();
    api.list.mockResolvedValue([eau(), sibling]);
    const page = await render(ProductFormPage, { api, params: { id: 'w1' } });
    await page.type('#format', '  ');

    await page.submit();

    expect(api.update).not.toHaveBeenCalled();
    expect(page.root.querySelector('#format-erreur')?.textContent).toContain('Donnez un format');
  });

  it('shows the server explanation under the format when the label is already taken', async () => {
    const api = fakeApi();
    api.list.mockResolvedValue([eau(), sibling]);
    api.update.mockRejectedValue(new ApiError(422, 'rule_violation', 'Le format « 1,5 L » existe déjà dans cette famille.'));
    const page = await render(ProductFormPage, { api, params: { id: 'w1' } });
    await page.type('#format', '1,5 L');

    await page.submit();

    expect(page.root.querySelector('#format-erreur')?.textContent).toContain('existe déjà');
    expect(page.navigate).not.toHaveBeenCalled();
  });

  it('offers to remove a photo only when the format has its own, not when it shows the family photo', async () => {
    const api = fakeApi();
    api.list.mockResolvedValue([eau({ photoId: 'ph-family', familyPhotoId: 'ph-family' })]);
    const familyOnly = await render(ProductFormPage, { api, params: { id: 'w1' } });
    expect(familyOnly.has('Retirer la photo')).toBe(false);
  });

  it('sends a photo for this format only, and can remove it', async () => {
    const api = fakeApi();
    api.list.mockResolvedValue([eau({ photoId: 'ph-own', familyPhotoId: 'ph-family' })]);
    const shrink = vi.fn().mockResolvedValue(new Blob(['x'], { type: 'image/jpeg' }));
    const page = await render(ProductFormPage, { api, params: { id: 'w1' }, extraProviders: [{ provide: ImageService, useValue: { shrink } }] });
    expect(page.has('Retirer la photo')).toBe(true);

    const input = page.root.querySelector<HTMLInputElement>('input[type="file"]')!;
    Object.defineProperty(input, 'files', { value: [new File(['x'], 'p.jpg', { type: 'image/jpeg' })], configurable: true });
    input.dispatchEvent(new Event('change'));
    await page.settle();
    expect(api.setPhoto).toHaveBeenCalledWith('format', 'w1', expect.any(Blob));

    await page.click('Retirer la photo');
    expect(api.removePhoto).toHaveBeenCalledWith('format', 'w1');
  });
});

describe('ProductFormPage: barcode scanner (ADR 0015)', () => {
  it('fills the barcode from a scan, whatever the keyboard layout, without saving the form', async () => {
    const api = fakeApi();
    api.list.mockResolvedValue([product({ id: 'p-9' })]);
    const page = await render(ProductFormPage, { api, params: { id: 'p-9' } });
    const field = page.root.querySelector<HTMLInputElement>('#code-barres')!;
    field.focus();

    const end = scanInto(field, '6111000000011');
    await page.fixture.whenStable();

    expect(field.value).toBe('6111000000011');
    expect(end!.defaultPrevented).toBe(true); // Entrée ne valide pas le formulaire
    expect(api.update).not.toHaveBeenCalled();
  });

  it('fills the barcode when the scan has no ending', async () => {
    const api = fakeApi();
    api.list.mockResolvedValue([product({ id: 'p-9' })]);
    const page = await render(ProductFormPage, { api, params: { id: 'p-9' } });
    const field = page.root.querySelector<HTMLInputElement>('#code-barres')!;
    field.focus();

    scanInto(field, '6111000000011', null);
    await new Promise((resolve) => setTimeout(resolve, 200));
    await page.fixture.whenStable();

    expect(field.value).toBe('6111000000011');
  });

  it('leaves the other fields alone: a scan typed in the name field is not taken', async () => {
    const api = fakeApi();
    api.list.mockResolvedValue([product({ id: 'p-9' })]);
    const page = await render(ProductFormPage, { api, params: { id: 'p-9' } });
    const name = page.root.querySelector<HTMLInputElement>('#nom')!;
    name.focus();

    scanInto(name, '6111000000011');
    await new Promise((resolve) => setTimeout(resolve, 200));

    expect(page.root.querySelector<HTMLInputElement>('#code-barres')!.value).toBe('111');
  });
});

describe('ProductFormPage: internal code (docs/specs/etiquettes-tickets ETQ-01)', () => {
  it('offers to create an internal code for a product with no barcode, and fills the field with it', async () => {
    const api = fakeApi();
    api.list.mockResolvedValue([product({ id: 'p-9', barcode: null })]);
    const page = await render(ProductFormPage, { api, params: { id: 'p-9' }, role: 'Manager' });

    await page.click('Créer un code interne');

    expect(api.createInternalBarcode).toHaveBeenCalledWith('p-9');
    expect(page.root.querySelector<HTMLInputElement>('#code-barres')!.value).toBe('2900000000018');
    expect(page.root.querySelector('[role="status"]')?.textContent).toContain('Code interne créé');
    expect(page.root.textContent).not.toContain('Créer un code interne');
  });

  it('does not offer it when the product already has a code', async () => {
    const api = fakeApi();
    api.list.mockResolvedValue([product({ id: 'p-9', barcode: '6111000000014' })]);
    const page = await render(ProductFormPage, { api, params: { id: 'p-9' }, role: 'Manager' });

    expect(page.root.textContent).not.toContain('Créer un code interne');
  });

  it('shows the refusal of the server in French', async () => {
    const api = fakeApi();
    api.list.mockResolvedValue([product({ id: 'p-9', barcode: null })]);
    api.createInternalBarcode.mockRejectedValue(new ApiError(403, 'forbidden', 'Seul un gérant peut créer un code-barres.'));
    const page = await render(ProductFormPage, { api, params: { id: 'p-9' }, role: 'Manager' });

    await page.click('Créer un code interne');

    expect(page.root.querySelector('[role="alert"]')?.textContent).toContain('pas le droit');
  });
});
