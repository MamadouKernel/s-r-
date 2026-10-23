import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ApiError } from '../sync/sync-api';
import { AuditApi } from './audit-api';

describe('AuditApi', () => {
  let api: AuditApi;
  let http: HttpTestingController;
  const range = { from: '2026-10-17T00:00:00.000Z', to: '2026-10-23T10:42:00.000Z' };

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    api = TestBed.inject(AuditApi);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('reads a page with the period, the paging and no category by default', async () => {
    const result = api.list({ range, category: null, limit: 50, offset: 0 });
    const request = http.expectOne((candidate) => candidate.url === '/api/v1/audit-log');
    expect(request.request.params.get('from')).toBe(range.from);
    expect(request.request.params.get('to')).toBe(range.to);
    expect(request.request.params.get('limit')).toBe('50');
    expect(request.request.params.get('offset')).toBe('0');
    expect(request.request.params.has('category')).toBe(false);
    request.flush({ entries: [], hasMore: false });
    expect(await result).toEqual({ entries: [], hasMore: false });
  });

  it('sends the category when one is chosen', async () => {
    const result = api.list({ range, category: 'sales', limit: 50, offset: 100 });
    const request = http.expectOne((candidate) => candidate.url === '/api/v1/audit-log');
    expect(request.request.params.get('category')).toBe('sales');
    expect(request.request.params.get('offset')).toBe('100');
    request.flush({ entries: [], hasMore: true });
    expect((await result).hasMore).toBe(true);
  });

  it('turns a refusal into an ApiError (a manager who may not read the journal)', async () => {
    const result = api.list({ range, category: null, limit: 50, offset: 0 });
    http.expectOne((candidate) => candidate.url === '/api/v1/audit-log').flush(
      { title: 'Action interdite', detail: 'Ce profil ne peut pas consulter le journal.', code: 'forbidden' },
      { status: 403, statusText: 'Forbidden' },
    );
    const error = await result.catch((caught: unknown) => caught);
    expect(error).toBeInstanceOf(ApiError);
    expect(error).toMatchObject({ status: 403, code: 'forbidden' });
  });
});
