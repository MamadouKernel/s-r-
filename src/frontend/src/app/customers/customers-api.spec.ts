import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { CustomersApi } from './customers-api';

describe('CustomersApi', () => {
  let api: CustomersApi;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    api = TestBed.inject(CustomersApi);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('lists the customers with the search text and the inactive flag', async () => {
    const list = api.list('awa', true);
    const request = http.expectOne((r) => r.url === '/api/v1/customers');
    expect(request.request.params.get('search')).toBe('awa');
    expect(request.request.params.get('includeInactive')).toBe('true');
    request.flush([{ id: 'c', name: 'Awa Koné' }]);
    expect((await list)[0].name).toBe('Awa Koné');

    const all = api.list(null);
    const second = http.expectOne((r) => r.url === '/api/v1/customers');
    expect(second.request.params.has('search')).toBe(false);
    expect(second.request.params.get('includeInactive')).toBe('false');
    second.flush([]);
    await all;
  });

  it('creates a customer with the contract field names', async () => {
    const created = api.create({ customerId: 'c-1', name: 'Awa Koné', phone: '0707070707', creditCeilingFcfa: null });
    const request = http.expectOne('/api/v1/customers');
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual({ customerId: 'c-1', name: 'Awa Koné', phone: '0707070707', creditCeilingFcfa: null });
    request.flush({ customerId: 'c-1', alreadyProcessed: false, creditCeilingFcfa: 20000 });
    expect((await created).creditCeilingFcfa).toBe(20000);
  });

  it('updates, deactivates and repays on the right routes', async () => {
    const updated = api.update('c-1', { name: 'Awa K.', phone: null, creditCeilingFcfa: 30000 });
    const put = http.expectOne('/api/v1/customers/c-1');
    expect(put.request.method).toBe('PUT');
    expect(put.request.body).toEqual({ name: 'Awa K.', phone: null, creditCeilingFcfa: 30000 });
    put.flush({ id: 'c-1' });
    await updated;

    const deactivated = api.deactivate('c-1');
    const post = http.expectOne('/api/v1/customers/c-1/deactivate');
    expect(post.request.method).toBe('POST');
    post.flush({ id: 'c-1' });
    await deactivated;

    const repaid = api.repay('c-1', { repaymentId: 'r-1', amountFcfa: 5000, method: 'Wave', reference: 'WV-1' });
    const repay = http.expectOne('/api/v1/customers/c-1/repayments');
    expect(repay.request.body).toEqual({ repaymentId: 'r-1', amountFcfa: 5000, method: 'Wave', reference: 'WV-1' });
    repay.flush({ repaymentId: 'r-1', debtFcfa: 3000, alreadyProcessed: false });
    expect((await repaid).debtFcfa).toBe(3000);
  });

  it('reads the repayment history', async () => {
    const history = api.repayments('c-1', 20);
    const request = http.expectOne((r) => r.url === '/api/v1/customers/c-1/repayments');
    expect(request.request.params.get('limit')).toBe('20');
    request.flush([{ id: 'r', amountFcfa: 1000, method: 'Cash', reference: null, userId: 'u', at: 'x' }]);
    expect(await history).toHaveLength(1);
  });

  it('turns the server refusal into an ApiError with its code and French message', async () => {
    const repay = api.repay('c-1', { repaymentId: 'r', amountFcfa: 9999, method: 'Cash', reference: null });
    http.expectOne('/api/v1/customers/c-1/repayments').flush(
      { code: 'rule_violation', detail: 'Le versement dépasse la dette du client (8 000 FCFA).' }, { status: 422, statusText: 'Unprocessable' },
    );
    await expect(repay).rejects.toMatchObject({ status: 422, code: 'rule_violation', message: 'Le versement dépasse la dette du client (8 000 FCFA).' });
  });
});
