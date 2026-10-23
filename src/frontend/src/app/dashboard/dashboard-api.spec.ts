import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ApiError } from '../sync/sync-api';
import { DashboardApi } from './dashboard-api';

describe('DashboardApi', () => {
  let api: DashboardApi;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    api = TestBed.inject(DashboardApi);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('reads the activity report of a period with the exact bounds', async () => {
    const result = api.activity({ from: '2026-10-23T00:00:00.000Z', to: '2026-10-23T10:42:00.000Z' });
    const request = http.expectOne((candidate) => candidate.url === '/api/v1/reports/activity');
    expect(request.request.params.get('from')).toBe('2026-10-23T00:00:00.000Z');
    expect(request.request.params.get('to')).toBe('2026-10-23T10:42:00.000Z');
    request.flush({ salesCount: 3, netTotalFcfa: 4500 });
    expect((await result).salesCount).toBe(3);
  });

  it('reads the open registers regardless of their opening date', async () => {
    const result = api.openSessions();
    const request = http.expectOne((candidate) => candidate.url === '/api/v1/register-sessions');
    expect(request.request.params.get('status')).toBe('Open');
    expect(request.request.params.has('from')).toBe(false);
    request.flush([]);
    await result;
  });

  it('reads the closed registers with a gap from the day before, since the server filters on opening time', async () => {
    const result = api.closedWithVariance('2026-10-22T00:00:00.000Z', '2026-10-23T10:43:00.000Z');
    const request = http.expectOne((candidate) => candidate.url === '/api/v1/register-sessions');
    expect(request.request.params.get('status')).toBe('Closed');
    expect(request.request.params.get('onlyWithVariance')).toBe('true');
    expect(request.request.params.get('from')).toBe('2026-10-22T00:00:00.000Z');
    expect(request.request.params.get('to')).toBe('2026-10-23T10:43:00.000Z');
    request.flush([]);
    await result;
  });

  it('turns a refusal into an ApiError (an accountant who may not read the activity, for instance)', async () => {
    const result = api.activity({ from: 'a', to: 'b' });
    http.expectOne((candidate) => candidate.url === '/api/v1/reports/activity').flush(
      { title: 'Action interdite', detail: 'Ce profil ne peut pas consulter les ventes et les rapports.', code: 'forbidden' },
      { status: 403, statusText: 'Forbidden' },
    );
    const error = await result.catch((caught: unknown) => caught);
    expect(error).toBeInstanceOf(ApiError);
    expect(error).toMatchObject({ status: 403, code: 'forbidden' });
  });
});
