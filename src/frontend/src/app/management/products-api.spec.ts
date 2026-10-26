import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ApiError } from '../sync/sync-api';
import { ProductsApi } from './products-api';

describe('ProductsApi', () => {
  let api: ProductsApi;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    api = TestBed.inject(ProductsApi);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('lists with a trimmed search and the inactive flag only when asked', async () => {
    const plain = api.list();
    http.expectOne((request) => request.url === '/api/v1/products' && request.params.keys().length === 0).flush([]);
    expect(await plain).toEqual([]);

    const filtered = api.list({ search: '  jus ', includeInactive: true });
    const request = http.expectOne((candidate) => candidate.url === '/api/v1/products');
    expect(request.request.params.get('search')).toBe('jus');
    expect(request.request.params.get('includeInactive')).toBe('true');
    request.flush([]);
    await filtered;
  });

  it('creates a product with its client id and the stock tracking choice', async () => {
    const created = api.create('p-1', {
      name: 'Jus', barcode: '111', category: 'Boissons', purchasePriceFcfa: 400, salePriceFcfa: 600, alertThreshold: 5, tracksStock: true,
    });
    const request = http.expectOne('/api/v1/products');
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual({
      productId: 'p-1', name: 'Jus', barcode: '111', category: 'Boissons', purchasePriceFcfa: 400, salePriceFcfa: 600, alertThreshold: 5, tracksStock: true,
    });
    request.flush({ productId: 'p-1', alreadyProcessed: false });
    await created;
  });

  it('updates, deactivates, receives and records a loss on the right routes', async () => {
    const update = api.update('p-1', { name: 'Jus', barcode: null, category: null, purchasePriceFcfa: 1, salePriceFcfa: 2, alertThreshold: 0 });
    const put = http.expectOne('/api/v1/products/p-1');
    expect(put.request.method).toBe('PUT');
    put.flush(null, { status: 204, statusText: 'No Content' });
    await update;

    const deactivate = api.deactivate('p-1');
    const post = http.expectOne('/api/v1/products/p-1/deactivate');
    expect(post.request.method).toBe('POST');
    post.flush(null, { status: 204, statusText: 'No Content' });
    await deactivate;

    const reception = api.receive('p-1', { movementId: 'm-1', quantity: 24, supplier: 'Grossiste', purchasePriceFcfa: null });
    const receptionRequest = http.expectOne('/api/v1/products/p-1/receptions');
    expect(receptionRequest.request.body).toEqual({ movementId: 'm-1', quantity: 24, supplier: 'Grossiste', purchasePriceFcfa: null });
    receptionRequest.flush({ movementId: 'm-1', quantityOnHand: 24, alreadyProcessed: false });
    expect((await reception).quantityOnHand).toBe(24);

    const loss = api.recordLoss('p-1', { movementId: 'm-2', quantity: 2, reason: 'Périmé' });
    http.expectOne('/api/v1/products/p-1/losses').flush({ movementId: 'm-2', quantityOnHand: 22, alreadyProcessed: false });
    expect((await loss).quantityOnHand).toBe(22);
  });

  it('reads the movements with a limit and the alerts', async () => {
    const movements = api.movements('p-1', 20);
    const request = http.expectOne((candidate) => candidate.url === '/api/v1/products/p-1/movements');
    expect(request.request.params.get('limit')).toBe('20');
    request.flush([]);
    await movements;

    const alerts = api.lowStock();
    http.expectOne('/api/v1/stock/alerts').flush([]);
    await alerts;
  });

  it('turns a refusal into an ApiError with the stable code (barcode already used)', async () => {
    const created = api.create('p-1', {
      name: 'Jus', barcode: '111', category: null, purchasePriceFcfa: 1, salePriceFcfa: 2, alertThreshold: 0, tracksStock: true,
    });
    http.expectOne('/api/v1/products').flush(
      { title: 'Conflit', detail: 'Ce code-barres est déjà utilisé par un autre produit.', code: 'barcode_taken' },
      { status: 409, statusText: 'Conflict' },
    );
    const error = await created.catch((caught: unknown) => caught);
    expect(error).toBeInstanceOf(ApiError);
    expect(error).toMatchObject({ status: 409, code: 'barcode_taken' });
  });

  const format = { productId: 'f-1', label: '1 L', barcode: null, purchasePriceFcfa: 180, salePriceFcfa: 300, alertThreshold: 3, tracksStock: true };

  it('creates a family with its formats, adds a format, renames a family and groups products on the right routes', async () => {
    const created = api.createFamily({ familyId: 'fam', name: 'Eau minérale', category: null, formats: [format] });
    const post = http.expectOne('/api/v1/product-families');
    expect(post.request.method).toBe('POST');
    expect(post.request.body).toEqual({ familyId: 'fam', name: 'Eau minérale', category: null, formats: [format] });
    post.flush({ familyId: 'fam', alreadyProcessed: false });
    await created;

    const added = api.addFormat('fam', format, '50 cl');
    const addRequest = http.expectOne('/api/v1/product-families/fam/formats');
    expect(addRequest.request.body).toEqual({ format, labelForExistingFormat: '50 cl' });
    addRequest.flush({ productId: 'f-1', alreadyProcessed: false });
    await added;

    const renamed = api.updateFamily('fam', { name: 'Eau de source', category: 'Eaux' });
    const put = http.expectOne('/api/v1/product-families/fam');
    expect(put.request.method).toBe('PUT');
    put.flush(null, { status: 204, statusText: 'No Content' });
    await renamed;

    const grouped = api.group({ familyId: 'g', name: 'Eau', category: null, members: [{ productId: 'a', label: '1 L' }] });
    const groupRequest = http.expectOne('/api/v1/product-families/group');
    expect(groupRequest.request.body.members).toEqual([{ productId: 'a', label: '1 L' }]);
    groupRequest.flush({ familyId: 'g', alreadyProcessed: false });
    await grouped;
  });

  it('sends a family photo or a format photo as raw bytes and returns the new photo id, and removes it', async () => {
    const image = new Blob(['x'], { type: 'image/jpeg' });

    const family = api.setPhoto('family', 'fam', image);
    const familyRequest = http.expectOne('/api/v1/product-families/fam/photo');
    expect(familyRequest.request.method).toBe('PUT');
    expect(familyRequest.request.body).toBe(image);
    expect(familyRequest.request.headers.get('Content-Type')).toBe('image/jpeg');
    familyRequest.flush({ photoId: 'ph-1' });
    expect(await family).toBe('ph-1');

    const own = api.setPhoto('format', 'f-1', image);
    http.expectOne('/api/v1/products/f-1/photo').flush({ photoId: 'ph-2' });
    expect(await own).toBe('ph-2');

    const removed = api.removePhoto('format', 'f-1');
    const del = http.expectOne('/api/v1/products/f-1/photo');
    expect(del.request.method).toBe('DELETE');
    del.flush(null, { status: 204, statusText: 'No Content' });
    await removed;
  });

  it('sends the format label with a product update', async () => {
    const update = api.update('p-1', { name: 'Eau', barcode: null, category: null, purchasePriceFcfa: 1, salePriceFcfa: 2, alertThreshold: 0, formatLabel: '1 L' });
    const put = http.expectOne('/api/v1/products/p-1');
    expect(put.request.body.formatLabel).toBe('1 L');
    put.flush(null, { status: 204, statusText: 'No Content' });
    await update;
  });

  it('asks the server for an internal code on the right route', async () => {
    const created = api.createInternalBarcode('p-1');
    const request = http.expectOne('/api/v1/products/p-1/internal-barcode');
    expect(request.request.method).toBe('POST');
    request.flush({ barcode: '2900000000018' });
    expect(await created).toBe('2900000000018');
  });
});
