import { ImageService } from '../core/image';
import { ApiError } from '../sync/sync-api';
import { FamilyFormPage } from './family-form-page';
import { fakeApi, render } from './test-support';

async function fillFormat(page: Awaited<ReturnType<typeof render>>, index: number, values: { label?: string; code?: string; buy?: string; sell: string; threshold?: string }) {
  if (values.label !== undefined) {
    await page.type(`#f${index}-format`, values.label);
  }
  if (values.code !== undefined) {
    await page.type(`#f${index}-code-barres`, values.code);
  }
  await page.type(`#f${index}-prix-achat`, values.buy ?? '0');
  await page.type(`#f${index}-prix-vente`, values.sell);
  await page.type(`#f${index}-seuil`, values.threshold ?? '0');
}

describe('FamilyFormPage', () => {
  beforeEach(() => {
    URL.createObjectURL = vi.fn(() => 'blob:preview');
    URL.revokeObjectURL = vi.fn();
  });

  it('creates a product with a single format and no label, then goes back to the list and confirms', async () => {
    const api = fakeApi();
    const page = await render(FamilyFormPage, { api });
    await page.type('#nom', ' Sel fin ');
    await page.type('#categorie', 'Épicerie');
    await fillFormat(page, 0, { code: ' 999 ', buy: '120', sell: '200', threshold: '5' });

    await page.submit();

    expect(api.createFamily).toHaveBeenCalledTimes(1);
    const input = api.createFamily.mock.calls[0][0];
    expect(input.familyId).toMatch(/^[0-9a-f-]{36}$/);
    expect(input).toMatchObject({ name: 'Sel fin', category: 'Épicerie' });
    expect(input.formats).toEqual([
      expect.objectContaining({ label: null, barcode: '999', purchasePriceFcfa: 120, salePriceFcfa: 200, alertThreshold: 5, tracksStock: true }),
    ]);
    expect(page.navigate).toHaveBeenCalledWith('/gestion/produits');
    expect(page.flash.current()).toBe('Produit créé.');
  });

  it('creates the rice with three formats in one operation', async () => {
    const api = fakeApi();
    const page = await render(FamilyFormPage, { api });
    await page.type('#nom', 'Riz parfumé');
    await page.click('Ajouter un autre format');
    await page.click('Ajouter un autre format');
    await fillFormat(page, 0, { label: '5 kg', buy: '2400', sell: '3200' });
    await fillFormat(page, 1, { label: '25 kg', buy: '11500', sell: '14500' });
    await fillFormat(page, 2, { label: '50 kg', buy: '22000', sell: '27000' });

    await page.submit();

    const formats = api.createFamily.mock.calls[0][0].formats;
    expect(formats.map((format: { label: string }) => format.label)).toEqual(['5 kg', '25 kg', '50 kg']);
    expect(formats.map((format: { salePriceFcfa: number }) => format.salePriceFcfa)).toEqual([3200, 14500, 27000]);
    expect(new Set(formats.map((format: { productId: string }) => format.productId)).size).toBe(3);
  });

  it('asks for a label on each format once there are two, and creates nothing', async () => {
    const api = fakeApi();
    const page = await render(FamilyFormPage, { api });
    await page.type('#nom', 'Eau');
    await page.click('Ajouter un autre format');
    await fillFormat(page, 0, { label: '1 L', sell: '300' });
    await fillFormat(page, 1, { sell: '400' });

    await page.submit();

    expect(api.createFamily).not.toHaveBeenCalled();
    expect(page.root.querySelector('#f1-format-erreur')?.textContent).toContain('Donnez un format');
    expect(page.root.querySelector('#f0-format-erreur')?.textContent?.trim()).toBe('');
  });

  it('explains an empty name and wrong numbers under their fields', async () => {
    const api = fakeApi();
    const page = await render(FamilyFormPage, { api });
    await fillFormat(page, 0, { sell: '12,5', buy: '-3', threshold: 'abc' });

    await page.submit();

    expect(api.createFamily).not.toHaveBeenCalled();
    expect(page.root.querySelector('#nom-erreur')?.textContent).toContain('obligatoire');
    expect(page.root.querySelector('#f0-prix-vente-erreur')?.textContent).toContain('chiffres');
    expect(page.root.querySelector('#f0-prix-achat-erreur')?.textContent).toContain('chiffres');
    expect(page.root.querySelector('#f0-seuil-erreur')?.textContent).toContain('entiers');
  });

  it('removes a format from the list but never the last one', async () => {
    const page = await render(FamilyFormPage, { api: fakeApi() });
    await page.click('Ajouter un autre format');
    expect(page.root.querySelectorAll('app-format-fields')).toHaveLength(2);

    await page.click('Retirer Format 2');

    expect(page.root.querySelectorAll('app-format-fields')).toHaveLength(1);
    expect(page.has('Retirer ce format')).toBe(false);
  });

  it('keeps the form and says a barcode is taken', async () => {
    const api = fakeApi();
    api.createFamily.mockRejectedValue(new ApiError(409, 'barcode_taken', ''));
    const page = await render(FamilyFormPage, { api });
    await page.type('#nom', 'Sel');
    await fillFormat(page, 0, { code: '999', sell: '200' });

    await page.submit();

    expect(page.root.querySelector('[role="alert"]')?.textContent).toContain('déjà utilisé');
    expect(page.navigate).not.toHaveBeenCalled();
    expect(page.root.querySelector<HTMLInputElement>('#nom')!.value).toBe('Sel');
  });

  it('keeps the same identifiers on a retry after a network failure, so nothing is created twice', async () => {
    const api = fakeApi();
    api.createFamily.mockRejectedValueOnce(new ApiError(0, 'network', ''));
    const page = await render(FamilyFormPage, { api });
    await page.type('#nom', 'Sel');
    await fillFormat(page, 0, { sell: '200' });

    await page.submit();
    expect(page.root.querySelector('[role="alert"]')?.textContent).toContain('injoignable');
    await page.submit();

    expect(api.createFamily).toHaveBeenCalledTimes(2);
    const [first, second] = api.createFamily.mock.calls.map((call) => call[0]);
    expect(second.familyId).toBe(first.familyId);
    expect(second.formats[0].productId).toBe(first.formats[0].productId);
    expect(page.navigate).toHaveBeenCalledWith('/gestion/produits');
  });

  describe('photo', () => {
    const shrink = vi.fn();

    async function pick(page: Awaited<ReturnType<typeof render>>) {
      const input = page.root.querySelector<HTMLInputElement>('input[type="file"]')!;
      Object.defineProperty(input, 'files', { value: [new File(['x'], 'photo.jpg', { type: 'image/jpeg' })], configurable: true });
      input.dispatchEvent(new Event('change'));
      await page.settle();
    }

    beforeEach(() => {
      shrink.mockReset();
      shrink.mockResolvedValue(new Blob(['small'], { type: 'image/jpeg' }));
    });

    it('sends the reduced photo after the product is created', async () => {
      const api = fakeApi();
      const page = await render(FamilyFormPage, { api, extraProviders: [{ provide: ImageService, useValue: { shrink } }] });
      await page.type('#nom', 'Eau');
      await fillFormat(page, 0, { sell: '300' });
      await pick(page);

      await page.submit();

      expect(api.setPhoto).toHaveBeenCalledTimes(1);
      const [owner, id, blob] = api.setPhoto.mock.calls[0];
      expect(owner).toBe('family');
      expect(id).toBe(api.createFamily.mock.calls[0][0].familyId);
      expect(blob.size).toBe(5);
      expect(page.flash.current()).toBe('Produit créé.');
    });

    it('creates the product without any photo, and sends nothing', async () => {
      const api = fakeApi();
      const page = await render(FamilyFormPage, { api, extraProviders: [{ provide: ImageService, useValue: { shrink } }] });
      await page.type('#nom', 'Eau');
      await fillFormat(page, 0, { sell: '300' });

      await page.submit();

      expect(api.setPhoto).not.toHaveBeenCalled();
      expect(page.navigate).toHaveBeenCalledWith('/gestion/produits');
    });

    it('still creates the product when the photo cannot be sent, and says so', async () => {
      const api = fakeApi();
      api.setPhoto.mockRejectedValue(new ApiError(0, 'network', ''));
      const page = await render(FamilyFormPage, { api, extraProviders: [{ provide: ImageService, useValue: { shrink } }] });
      await page.type('#nom', 'Eau');
      await fillFormat(page, 0, { sell: '300' });
      await pick(page);

      await page.submit();

      expect(page.navigate).toHaveBeenCalledWith('/gestion/produits');
      expect(page.flash.current()).toContain('la photo n’a pas pu être envoyée');
    });

    it('shows the reason when the image cannot be used, and keeps going without it', async () => {
      shrink.mockRejectedValue(new Error('Cette image est trop lourde. Choisissez-en une plus petite.'));
      const api = fakeApi();
      const page = await render(FamilyFormPage, { api, extraProviders: [{ provide: ImageService, useValue: { shrink } }] });
      await pick(page);

      expect(page.root.querySelector('[role="alert"]')?.textContent).toContain('trop lourde');
    });

    it('lets the user take the photo back before saving', async () => {
      const api = fakeApi();
      const page = await render(FamilyFormPage, { api, extraProviders: [{ provide: ImageService, useValue: { shrink } }] });
      await page.type('#nom', 'Eau');
      await fillFormat(page, 0, { sell: '300' });
      await pick(page);

      await page.click('Retirer la photo');
      await page.submit();

      expect(api.setPhoto).not.toHaveBeenCalled();
    });
  });
});
