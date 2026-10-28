import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { DayReportApi } from './day-report-api';

describe('DayReportApi', () => {
  it('reads the report of the chosen day and turns a refusal into an error with its status', async () => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    const api = TestBed.inject(DayReportApi);
    const http = TestBed.inject(HttpTestingController);

    const report = api.day('2026-10-28');
    const request = http.expectOne((candidate) => candidate.url === '/api/v1/reports/day');
    expect(request.request.params.get('date')).toBe('2026-10-28');
    request.flush({ day: '2026-10-28', salesCount: 4 });
    expect((await report).salesCount).toBe(4);

    const refused = api.day('2026-10-28');
    http.expectOne((candidate) => candidate.url === '/api/v1/reports/day').flush({ code: 'forbidden', detail: 'Non.' }, { status: 403, statusText: 'Forbidden' });
    await expect(refused).rejects.toMatchObject({ status: 403 });
    http.verify();
  });
});
