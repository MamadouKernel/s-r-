import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ImportApi, ImportReport } from './import-api';

const report = (overrides: Partial<ImportReport> = {}): ImportReport => ({
  rowsRead: 3, newProducts: 2, newFormats: 3, stockMovements: 2, skippedCount: 0, skipped: [], problemCount: 0, problems: [], canImport: true,
  imported: false, alreadyProcessed: false, ...overrides,
});

describe('ImportApi', () => {
  let api: ImportApi;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    api = TestBed.inject(ImportApi);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('downloads the template as a file', async () => {
    const result = api.template();
    const request = http.expectOne('/api/v1/product-imports/template');
    expect(request.request.responseType).toBe('blob');
    request.flush(new Blob(['xlsx']));

    expect((await result).size).toBe(4);
  });

  it('sends the file as raw bytes for the check and for the import, with the import identifier', async () => {
    const file = new Blob(['x'], { type: 'application/octet-stream' });

    const preview = api.preview(file);
    const previewRequest = http.expectOne('/api/v1/product-imports/preview');
    expect(previewRequest.request.method).toBe('POST');
    expect(previewRequest.request.body).toBe(file);
    expect(previewRequest.request.headers.get('Content-Type')).toContain('spreadsheetml');
    previewRequest.flush(report());
    expect((await preview).newProducts).toBe(2);

    const done = api.import('abc-1', file);
    const importRequest = http.expectOne('/api/v1/product-imports?importId=abc-1');
    importRequest.flush(report({ imported: true }));
    expect((await done).imported).toBe(true);
  });

  it('gives the report of an import refused for lines to correct, instead of an error', async () => {
    const problems = report({ problemCount: 1, canImport: false, problems: [{ line: 3, column: 'Prix de vente', message: 'le prix…', text: 'Ligne 3 : le prix…' }] });

    const result = api.import('abc-2', new Blob(['x']));
    http.expectOne('/api/v1/product-imports?importId=abc-2').flush(problems, { status: 422, statusText: 'Unprocessable Entity' });

    expect((await result).problems[0].line).toBe(3);
  });

  it('turns a refused file (not an xlsx, too heavy) into an ApiError carrying the French message', async () => {
    const result = api.preview(new Blob(['x']));
    http.expectOne('/api/v1/product-imports/preview').flush(
      { title: 'Règle métier non respectée', detail: 'Ce fichier n’est pas un classeur Excel (.xlsx).', code: 'rule_violation' },
      { status: 422, statusText: 'Unprocessable Entity' },
    );

    await expect(result).rejects.toMatchObject({ status: 422, code: 'rule_violation', message: 'Ce fichier n’est pas un classeur Excel (.xlsx).' });
  });

  it('reports an unreachable server as a network error', async () => {
    const result = api.preview(new Blob(['x']));
    http.expectOne('/api/v1/product-imports/preview').error(new ProgressEvent('error'));

    await expect(result).rejects.toMatchObject({ status: 0 });
  });
});
