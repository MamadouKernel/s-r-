import { ImageService } from '../core/image';
import { ApiError } from '../sync/sync-api';
import { FamilyPage } from './family-page';
import { fakeApi, product, render } from './test-support';

const water = [
  product({ id: 'w15', name: 'Eau minérale · 1,5 L', formatLabel: '1,5 L', familyId: 'eau', familyName: 'Eau minérale', salePriceFcfa: 400, barcode: '111', quantityOnHand: 12, familyPhotoId: 'ph-eau', photoId: 'ph-eau' }),
  product({ id: 'w1', name: 'Eau minérale · 1 L', formatLabel: '1 L', familyId: 'eau', familyName: 'Eau minérale', salePriceFcfa: 300, barcode: '222', quantityOnHand: 3, familyPhotoId: 'ph-eau', photoId: 'ph-eau' }),
  product({ id: 'w30', name: 'Eau minérale · 30 cl', formatLabel: '30 cl', familyId: 'eau', familyName: 'Eau minérale', salePriceFcfa: 150, barcode: null, quantityOnHand: 0, familyPhotoId: 'ph-eau', photoId: 'ph-eau' }),
];
const salt = product({ id: 'sel', name: 'Sel fin', familyId: 'sel', familyName: 'Sel fin', formatLabel: null, salePriceFcfa: 200 });
const others = [product({ id: 'x', name: 'Autre', familyId: 'autre', familyName: 'Autre' })];

async function open(familyId: string, items = [...water, ...others, salt], api = fakeApi()) {
  api.list.mockResolvedValue(items);
  const page = await render(FamilyPage, { api, params: { familyId }, extraProviders: [{ provide: ImageService, useValue: { shrink: vi.fn().mockResolvedValue(new Blob(['x'], { type: 'image/jpeg' })) } }] });
  return { api, page };
}

describe('FamilyPage', () => {
  it('shows the family with its formats from the cheapest, and nothing of the other products', async () => {
    const { page } = await open('eau');

    expect(page.root.querySelector('#nom')).toBeTruthy();
    expect(page.root.querySelector<HTMLInputElement>('#nom')!.value).toBe('Eau minérale');
    const rows = [...page.root.querySelectorAll('#formats-titre ~ ul > li')].map((row) => row.textContent ?? '');
    expect(rows).toHaveLength(3);
    expect(rows[0]).toContain('30 cl');
    expect(rows[2]).toContain('1,5 L');
    expect(page.has('Autre')).toBe(false);
  });

  it('renames the family and confirms', async () => {
    const { api, page } = await open('eau');
    await page.type('#nom', ' Eau de source ');
    await page.type('#categorie', 'Eaux');

    await page.submit();

    expect(api.updateFamily).toHaveBeenCalledWith('eau', { name: 'Eau de source', category: 'Eaux' });
    expect(page.flash.current()).toBe('Famille modifiée.');
  });

  it('refuses an empty name', async () => {
    const { api, page } = await open('eau');
    await page.type('#nom', '  ');

    await page.submit();

    expect(api.updateFamily).not.toHaveBeenCalled();
    expect(page.root.querySelector('#nom-erreur')?.textContent).toContain('obligatoire');
  });

  it('adds a format to a family that already has several', async () => {
    const { api, page } = await open('eau');
    await page.click('Ajouter un format');
    expect(page.root.querySelector('#premier-format')).toBeNull();
    await page.type('#nf-format', '5 L');
    await page.type('#nf-prix-achat', '500');
    await page.type('#nf-prix-vente', '900');

    await page.submit(1);

    expect(api.addFormat).toHaveBeenCalledTimes(1);
    const [family, format, first] = api.addFormat.mock.calls[0];
    expect(family).toBe('eau');
    expect(format).toMatchObject({ label: '5 L', purchasePriceFcfa: 500, salePriceFcfa: 900, tracksStock: true });
    expect(first).toBeNull();
    expect(page.flash.current()).toBe('Format ajouté.');
  });

  it('asks for the label of the current product when it is the only format and has none', async () => {
    const { api, page } = await open('sel');
    await page.click('Ajouter un format');
    await page.type('#nf-format', '1 kg');
    await page.type('#nf-prix-vente', '900');

    await page.submit(1);
    expect(api.addFormat).not.toHaveBeenCalled();
    expect(page.root.querySelector('#premier-format-erreur')?.textContent).toContain('Donnez un format');

    await page.type('#premier-format', '500 g');
    await page.submit(1);

    expect(api.addFormat.mock.calls[0][2]).toBe('500 g');
  });

  it('requires a label on the new format and explains a barcode already used', async () => {
    const { api, page } = await open('eau');
    await page.click('Ajouter un format');
    await page.type('#nf-prix-vente', '900');
    await page.submit(1);
    expect(page.root.querySelector('#nf-format-erreur')?.textContent).toContain('Donnez un format');

    api.addFormat.mockRejectedValue(new ApiError(409, 'barcode_taken', ''));
    await page.type('#nf-format', '5 L');
    await page.submit(1);

    expect(page.root.querySelector('[role="alert"]')?.textContent).toContain('déjà utilisé');
  });

  it('keeps the same product identifier when adding is retried after a network failure', async () => {
    const { api, page } = await open('eau');
    api.addFormat.mockRejectedValueOnce(new ApiError(0, 'network', ''));
    await page.click('Ajouter un format');
    await page.type('#nf-format', '5 L');
    await page.type('#nf-prix-vente', '900');

    await page.submit(1);
    await page.submit(1);

    expect(api.addFormat).toHaveBeenCalledTimes(2);
    expect(api.addFormat.mock.calls[1][1].productId).toBe(api.addFormat.mock.calls[0][1].productId);
  });

  it('changes and removes the family photo', async () => {
    const { api, page } = await open('eau');
    const input = page.root.querySelector<HTMLInputElement>('input[type="file"]')!;
    Object.defineProperty(input, 'files', { value: [new File(['x'], 'p.jpg', { type: 'image/jpeg' })], configurable: true });
    input.dispatchEvent(new Event('change'));
    await page.settle();

    expect(api.setPhoto).toHaveBeenCalledWith('family', 'eau', expect.any(Blob));

    await page.click('Retirer la photo');
    expect(api.removePhoto).toHaveBeenCalledWith('family', 'eau');
  });

  it('offers no « Retirer la photo » when the family has none', async () => {
    const { page } = await open('sel');

    expect(page.has('Retirer la photo')).toBe(false);
  });

  it('says so when the product does not exist', async () => {
    const { page } = await open('inconnu');

    expect(page.root.querySelector('[role="alert"]')?.textContent).toContain('introuvable');
  });

  it('links each format to its own edit and stock pages', async () => {
    const { page } = await open('eau');

    const links = [...page.root.querySelectorAll('a')].map((link) => link.getAttribute('href'));
    expect(links).toContain('/gestion/produits/w1');
    expect(links).toContain('/gestion/produits/w1/stock');
  });
});
