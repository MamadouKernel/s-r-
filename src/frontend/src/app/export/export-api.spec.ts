import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ExportApi, fileNameOf } from './export-api';

describe('fileNameOf', () => {
  it.each([
    ['attachment; filename=soro-export-2026-10-25.xlsx; filename*=UTF-8\'\'soro-export-2026-10-25.xlsx', 'soro-export-2026-10-25.xlsx'],
    ['attachment; filename="soro-export-2026-10-25.xlsx"', 'soro-export-2026-10-25.xlsx'],
    ['attachment; filename=export.xlsx', 'export.xlsx'],
  ])('reads %s', (header, expected) => expect(fileNameOf(header)).toBe(expected));

  it('gives nothing when there is no name', () => {
    expect(fileNameOf(null)).toBeNull();
    expect(fileNameOf('attachment')).toBeNull();
  });
});

describe('ExportApi', () => {
  let api: ExportApi;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    api = TestBed.inject(ExportApi);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('asks for the period as dates and gives the file with the name the server proposes', async () => {
    const result = api.download('2026-01-01', '2026-10-25');
    const request = http.expectOne((candidate) => candidate.url === '/api/v1/data-export');
    expect(request.request.params.get('from')).toBe('2026-01-01');
    expect(request.request.params.get('to')).toBe('2026-10-25');
    expect(request.request.responseType).toBe('blob');
    request.flush(new Blob(['xlsx']), { headers: { 'Content-Disposition': 'attachment; filename=soro-export-2026-10-25.xlsx' } });

    const file = await result;
    expect(file.fileName).toBe('soro-export-2026-10-25.xlsx');
    expect(file.blob.size).toBe(4);
  });

  it('sends no period when none is chosen, and falls back to a default name', async () => {
    const result = api.download(null, null);
    const request = http.expectOne((candidate) => candidate.url === '/api/v1/data-export');
    expect(request.request.params.keys()).toEqual([]);
    request.flush(new Blob(['x']));

    expect((await result).fileName).toBe('soro-export.xlsx');
  });

  it('keeps the French message of the server when the answer was a refusal sent as a file', async () => {
    const result = api.download('2026-01-01', '2026-10-25');
    const body = new Blob([JSON.stringify({ code: 'too_many_requests', detail: 'Vous avez demandé plusieurs exports : réessayez dans 12 minutes.' })], { type: 'application/problem+json' });
    http.expectOne((candidate) => candidate.url === '/api/v1/data-export').flush(body, { status: 429, statusText: 'Too Many Requests' });

    await expect(result).rejects.toMatchObject({ status: 429, code: 'too_many_requests', message: 'Vous avez demandé plusieurs exports : réessayez dans 12 minutes.' });
  });

  it('reports an unreachable server as a network error', async () => {
    const result = api.download(null, null);
    http.expectOne((candidate) => candidate.url === '/api/v1/data-export').error(new ProgressEvent('error'));

    await expect(result).rejects.toMatchObject({ status: 0 });
  });
});
