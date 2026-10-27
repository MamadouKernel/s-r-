import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { SalesApi } from './sales-api';

describe('SalesApi', () => {
  let api: SalesApi;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    api = TestBed.inject(SalesApi);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('lists the recent sales, most recent first, with a limit', async () => {
    const list = api.list();
    const request = http.expectOne((candidate) => candidate.url === '/api/v1/sales');
    expect(request.request.params.get('limit')).toBe('100');
    request.flush([{ id: 's-1', ticket: 12 }]);
    expect((await list)[0].ticket).toBe(12);
  });

  it('reads one sale with its returns', async () => {
    const detail = api.detail('s-1');
    http.expectOne('/api/v1/sales/s-1').flush({ id: 's-1', returns: [] });
    expect((await detail).id).toBe('s-1');
  });

  it('asks the approval of a manager with the identifier, the pin, the sale and the purpose', async () => {
    const granted = api.grant({ identifier: 'gerant', pin: '4821', saleId: 's-1', purpose: 'discount', amountFcfa: 300 });
    const request = http.expectOne('/api/v1/approvals');
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual({ identifier: 'gerant', pin: '4821', subjectId: 's-1', purpose: 'discount', amount: 300 });
    request.flush({ token: 'sceau', approverId: 'm', approverName: 'Kofi', expiresAt: 'x' });
    expect((await granted).approverName).toBe('Kofi');
  });

  it('cancels a sale with the operation, the register, the choice for the goods and the approval', async () => {
    const done = api.cancel('s-1', { operationId: 'op-1', registerId: 'reg', reason: 'Erreur', resellable: false, approvalToken: 'sceau' });
    const request = http.expectOne('/api/v1/sales/s-1/cancel');
    expect(request.request.body).toEqual({ operationId: 'op-1', registerId: 'reg', reason: 'Erreur', resellable: false, approvalToken: 'sceau' });
    request.flush({ returnId: 'op-1', totalFcfa: 2000, cashFcfa: 2000, creditFcfa: 0, refund: [], alreadyProcessed: false });
    expect((await done).totalFcfa).toBe(2000);
  });

  it('returns lines of a sale on the right route', async () => {
    const done = api.returnLines('s-1', { operationId: 'op-2', registerId: 'reg', reason: 'Client mécontent', lines: [{ lineIndex: 0, quantity: 1, resellable: true }], approvalToken: null });
    const request = http.expectOne('/api/v1/sales/s-1/returns');
    expect(request.request.body.lines).toEqual([{ lineIndex: 0, quantity: 1, resellable: true }]);
    request.flush({ returnId: 'op-2', totalFcfa: 1000, cashFcfa: 1000, creditFcfa: 0, refund: [], alreadyProcessed: false });
    expect((await done).cashFcfa).toBe(1000);
  });

  it('turns a refusal of the server into an error with its code', async () => {
    const granted = api.grant({ identifier: 'g', pin: '0', saleId: 's', purpose: 'reversal', amountFcfa: 0 });
    http.expectOne('/api/v1/approvals').flush({ code: 'invalid_credentials', detail: 'Identifiant ou code PIN incorrect.' }, { status: 401, statusText: 'Unauthorized' });
    await expect(granted).rejects.toMatchObject({ status: 401, code: 'invalid_credentials' });
  });
});
