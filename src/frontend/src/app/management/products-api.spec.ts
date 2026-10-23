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
});
