import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ApiService } from './api.service';
import { ApiError } from '../sync/sync-api';
import { SaleOperation } from '../data/journal';

const sale: SaleOperation = {
  kind: 'sale', seq: 7, status: 'pending', createdAt: '2026-10-23T08:10:00.000Z', saleId: 'sale-1', sessionId: 's-1',
  at: '2026-10-23T08:10:00.000Z', discountFcfa: 0, netTotalFcfa: 2100, changeFcfa: 0,
  lines: [{ productId: 'p', name: 'Jus', unitPriceFcfa: 600, quantity: 3 }, { productId: 'q', name: 'Savon', unitPriceFcfa: 300, quantity: 1 }],
  payments: [
    { id: 'pay-1', method: 'Cash', amountFcfa: 600, tenderedFcfa: 1000 },
    { id: 'pay-2', method: 'Wave', amountFcfa: 1500, reference: ' W-1 ' },
  ],
};

describe('ApiService', () => {
  let api: ApiService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    api = TestBed.inject(ApiService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('sends the sales with the local sequence and the contract field names', async () => {
    const result = api.syncSales([sale]);
    const request = http.expectOne('/api/v1/sales/sync');
    expect(request.request.method).toBe('POST');
    expect(request.request.body.sales[0]).toEqual({
      saleId: 'sale-1', sessionId: 's-1', localSequence: 7, at: '2026-10-23T08:10:00.000Z', discountFcfa: 0, customerId: null,
      lines: [
        { productId: 'p', name: 'Jus', quantity: 3, unitPriceFcfa: 600 },
        { productId: 'q', name: 'Savon', quantity: 1, unitPriceFcfa: 300 },
      ],
      payments: [
        { paymentId: 'pay-1', method: 'Cash', amountFcfa: 600, tenderedFcfa: 1000, reference: null },
        { paymentId: 'pay-2', method: 'Wave', amountFcfa: 1500, tenderedFcfa: null, reference: 'W-1' },
      ],
    });
    request.flush({ statuses: [{ saleId: 'sale-1', outcome: 'Accepted', errorCode: null, message: null }] });
    expect(await result).toEqual([{ saleId: 'sale-1', outcome: 'Accepted', errorCode: null, message: null }]);
  });

  it('keeps only active products of the catalogue and drops the fields the cashier must not see', async () => {
    const result = api.products();
    http.expectOne('/api/v1/products').flush([
      { id: 'a', name: 'Jus', barcode: '1', category: 'B', purchasePriceFcfa: 400, salePriceFcfa: 600, alertThreshold: 5, tracksStock: true, isActive: true, quantityOnHand: 9, isLowStock: false },
      { id: 'b', name: 'Ancien', barcode: null, category: null, purchasePriceFcfa: null, salePriceFcfa: 100, alertThreshold: 0, tracksStock: false, isActive: false, quantityOnHand: 0, isLowStock: false },
    ]);
    expect(await result).toEqual([
      { id: 'a', name: 'Jus', barcode: '1', category: 'B', salePriceFcfa: 600, tracksStock: true, quantityOnHand: 9 },
    ]);
  });

  it('turns a problem+json response into an ApiError with the stable code and the French detail', async () => {
    const result = api.login('tenant', 'awa', '1234');
    http.expectOne('/api/v1/auth/login').flush(
      { title: 'Identifiants invalides', detail: 'Identifiant ou PIN incorrect.', code: 'invalid_credentials' },
      { status: 401, statusText: 'Unauthorized' },
    );
    await expect(result).rejects.toMatchObject({ status: 401, code: 'invalid_credentials', message: 'Identifiant ou PIN incorrect.' });
  });

  it('sends the device identity with the login when the device has one, and nothing otherwise', async () => {
    const withDevice = api.login('tenant', 'awa', '1234', { id: 'dev', secret: 'sec' });
    const request = http.expectOne('/api/v1/auth/login');
    expect(request.request.body).toEqual({ tenantId: 'tenant', identifier: 'awa', pin: '1234', deviceId: 'dev', deviceSecret: 'sec' });
    request.flush({ accessToken: 'jwt' });
    await withDevice;

    const without = api.login('tenant', 'awa', '1234');
    const second = http.expectOne('/api/v1/auth/login');
    expect(second.request.body).toEqual({ tenantId: 'tenant', identifier: 'awa', pin: '1234' });
    second.flush({ accessToken: 'jwt' });
    await without;
  });

  it('reports an unreachable server as status 0', async () => {
    const result = api.products();
    http.expectOne('/api/v1/products').error(new ProgressEvent('error'));
    const error = await result.catch((caught: unknown) => caught);
    expect(error).toBeInstanceOf(ApiError);
    expect(error).toMatchObject({ status: 0, code: 'network' });
  });

  it('posts the session opening and the closing with their identifiers', async () => {
    const opening = api.openSession({
      kind: 'openSession', seq: 1, status: 'pending', createdAt: 'x', sessionId: 's-1', pointOfSaleId: 'pos', registerId: 'reg',
      openingFloatFcfa: 10000, openedAt: '2026-10-23T08:00:00.000Z',
    });
    const open = http.expectOne('/api/v1/register-sessions');
    expect(open.request.body).toEqual({
      sessionId: 's-1', pointOfSaleId: 'pos', registerId: 'reg', openingFloatFcfa: 10000, openedAt: '2026-10-23T08:00:00.000Z',
    });
    open.flush({ sessionId: 's-1', alreadyProcessed: false });
    await opening;

    const closing = api.closeSession({
      kind: 'closeSession', seq: 9, status: 'pending', createdAt: 'x', sessionId: 's-1', countedCashFcfa: 11300, at: '2026-10-23T17:00:00.000Z',
    });
    const close = http.expectOne('/api/v1/register-sessions/s-1/close');
    expect(close.request.body).toEqual({ countedCashFcfa: 11300, at: '2026-10-23T17:00:00.000Z' });
    close.flush({ expectedCashFcfa: 11800, countedCashFcfa: 11300, varianceFcfa: -500, alreadyProcessed: false });
    expect((await closing).varianceFcfa).toBe(-500);
  });
});
