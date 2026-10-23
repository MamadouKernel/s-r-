import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { DashboardApi } from './dashboard-api';

describe('DashboardApi: top products', () => {
  let api: DashboardApi;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    api = TestBed.inject(DashboardApi);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('asks for the ranking with the period, the criterion and the limit', async () => {
    const result = api.topProducts({ from: '2026-10-17T00:00:00.000Z', to: '2026-10-23T10:42:00.000Z' }, 'margin', 5);
    const request = http.expectOne((candidate) => candidate.url === '/api/v1/reports/top-products');
    expect(request.request.params.get('from')).toBe('2026-10-17T00:00:00.000Z');
    expect(request.request.params.get('to')).toBe('2026-10-23T10:42:00.000Z');
    expect(request.request.params.get('by')).toBe('margin');
    expect(request.request.params.get('limit')).toBe('5');
    request.flush([{ productId: 'p', name: 'Jus', quantity: 10, revenueFcfa: 6000, marginFcfa: null }]);
    expect((await result)[0].marginFcfa).toBeNull();
  });
});
