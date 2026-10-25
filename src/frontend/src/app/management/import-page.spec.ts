import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { signal } from '@angular/core';
import { ApiError } from '../sync/sync-api';
import { NetworkService } from '../services/network.service';
import { ImportApi, ImportReport } from './import-api';
import { ImportPage } from './import-page';

const report = (overrides: Partial<ImportReport> = {}): ImportReport => ({
  rowsRead: 42, newProducts: 18, newFormats: 40, stockMovements: 31, skippedCount: 0, skipped: [], problemCount: 0, problems: [], canImport: true,
  imported: false, alreadyProcessed: false, ...overrides,
});

function fakeApi() {
  return {
    template: vi.fn().mockResolvedValue(new Blob(['x'])),
    preview: vi.fn().mockResolvedValue(report()),
    import: vi.fn().mockResolvedValue(report({ imported: true })),
  };
}

async function open(api = fakeApi(), online = true) {
  TestBed.configureTestingModule({
    providers: [
      provideRouter([]),
      { provide: ImportApi, useValue: api },
      { provide: NetworkService, useValue: { online: signal(online) } },
    ],
  });
  const fixture = TestBed.createComponent(ImportPage);
  const root = fixture.nativeElement as HTMLElement;
  const settle = async () => {
    await fixture.whenStable();
    await new Promise((resolve) => setTimeout(resolve, 5));
    await fixture.whenStable();
  };
  await settle();
  return {
    api, root, settle,
    has: (text: string) => (root.textContent ?? '').replace(/\s+/g, ' ').includes(text),
    pick: async (name = 'produits.xlsx') => {
      const input = root.querySelector<HTMLInputElement>('input[type="file"]')!;
      Object.defineProperty(input, 'files', { value: [new File(['x'], name)], configurable: true });
      input.dispatchEvent(new Event('change'));
      await settle();
    },
    click: async (label: string) => {
      const button = [...root.querySelectorAll('button')].find((candidate) => (candidate.textContent ?? '').includes(label));
      expect(button, `bouton « ${label} » introuvable`).toBeTruthy();
      button!.click();
      await settle();
    },
  };
}

describe('ImportPage', () => {
  beforeEach(() => {
    URL.createObjectURL = vi.fn(() => 'blob:modele');
    URL.revokeObjectURL = vi.fn();
  });

  it('starts with the template and the file choice, and offers no import button yet', async () => {
    const page = await open();

    expect(page.has('Télécharger le modèle Excel')).toBe(true);
    expect(page.has('Choisir un fichier')).toBe(true);
    expect(page.root.querySelector('input[type="file"]')!.getAttribute('accept')).toContain('.xlsx');
    expect(page.has('Importer ces')).toBe(false);
  });

  it('downloads the template as an xlsx file', async () => {
    const click = vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => undefined);
    const page = await open();

    await page.click('Télécharger le modèle Excel');

    expect(page.api.template).toHaveBeenCalledTimes(1);
    expect(click).toHaveBeenCalled();
    click.mockRestore();
  });

  it('says so when the template cannot be downloaded', async () => {
    const api = fakeApi();
    api.template.mockRejectedValue(new ApiError(0, 'network', ''));
    const page = await open(api);

    await page.click('Télécharger le modèle Excel');

    expect(page.root.querySelector('[role="alert"]')?.textContent).toContain('injoignable');
  });

  it('checks the chosen file at once and shows what will be created, with the import button', async () => {
    const page = await open();

    await page.pick('produits-octobre.xlsx');

    expect(page.api.preview).toHaveBeenCalledTimes(1);
    expect(page.has('produits-octobre.xlsx')).toBe(true);
    expect(page.has('Votre fichier est prêt')).toBe(true);
    const numbers = [...page.root.querySelectorAll('dd')].map((cell) => cell.textContent?.trim());
    expect(numbers).toEqual(['18', '40', '31', '0']);
    expect(page.has('Importer ces 18 produits')).toBe(true);
    expect(page.api.import).not.toHaveBeenCalled();
  });

  it('lists every line to correct, with the line number, the column and what to fix, and offers no import', async () => {
    const api = fakeApi();
    api.preview.mockResolvedValue(report({
      canImport: false, problemCount: 3,
      problems: [
        { line: 5, column: 'Prix de vente', message: 'le prix de vente doit être un nombre entier en FCFA, sans centimes.', text: 'Ligne 5 : …' },
        { line: 12, column: 'Format', message: 'donnez un format à chaque ligne de ce produit.', text: 'Ligne 12 : …' },
        { line: 31, column: 'Code-barres', message: 'Excel a transformé ce code-barres en nombre.', text: 'Ligne 31 : …' },
      ],
    }));
    const page = await open(api);

    await page.pick();

    expect(page.has('3 lignes sont à corriger')).toBe(true);
    expect(page.has('Rien n’a été importé')).toBe(true);
    const rows = [...page.root.querySelectorAll('tbody tr')].map((row) => row.textContent ?? '');
    expect(rows).toHaveLength(3);
    expect(rows[0]).toContain('5');
    expect(rows[0]).toContain('Prix de vente');
    expect(rows[0]).toContain('Le prix de vente doit être un nombre entier');
    expect(page.has('Importer ces')).toBe(false);
    expect(page.has('Choisir un autre fichier')).toBe(true);
  });

  it('says how many more lines need correcting when only the first fifty are listed', async () => {
    const api = fakeApi();
    const problems = Array.from({ length: 50 }, (_, index) => ({ line: index + 2, column: 'Prix de vente', message: 'x', text: '' }));
    api.preview.mockResolvedValue(report({ canImport: false, problemCount: 80, problems }));
    const page = await open(api);

    await page.pick();

    expect(page.has('… et 30 autres lignes à corriger.')).toBe(true);
  });

  it('shows the ignored products as information, not as an error', async () => {
    const api = fakeApi();
    api.preview.mockResolvedValue(report({ skippedCount: 2, skipped: [{ line: 7, name: 'Savon Omo', reason: 'ce produit est déjà dans votre catalogue.' }, { line: 8, name: 'Sel fin', reason: 'ce code-barres est déjà dans votre catalogue.' }] }));
    const page = await open(api);

    await page.pick();

    expect(page.has('Votre fichier est prêt')).toBe(true);
    expect(page.has('Ligne 7 : Savon Omo — Ce produit est déjà dans votre catalogue.')).toBe(true);
    expect(page.root.querySelector('[role="alert"]')).toBeNull();
  });

  it('says there is nothing to import when everything is already in the catalogue, or when the file is empty', async () => {
    const api = fakeApi();
    api.preview.mockResolvedValue(report({ canImport: false, newProducts: 0, newFormats: 0, skippedCount: 3 }));
    const page = await open(api);
    await page.pick();
    expect(page.has('Tous les produits de ce fichier sont déjà dans votre catalogue')).toBe(true);
    expect(page.has('Importer ces')).toBe(false);

    TestBed.resetTestingModule();
    const emptyApi = fakeApi();
    emptyApi.preview.mockResolvedValue(report({ canImport: false, newProducts: 0, newFormats: 0, rowsRead: 0 }));
    const empty = await open(emptyApi);
    await empty.pick();
    expect(empty.has('Ce fichier ne contient aucun produit')).toBe(true);
  });

  it('imports the same file with the identifier fixed at the choice, then confirms in plain words', async () => {
    const page = await open();
    await page.pick();

    await page.click('Importer ces 18 produits');

    expect(page.api.import).toHaveBeenCalledTimes(1);
    const [id, file] = page.api.import.mock.calls[0];
    expect(id).toMatch(/^[0-9a-f-]{36}$/);
    expect((file as File).name).toBe('produits.xlsx');
    expect(page.has('L’importation est terminée.')).toBe(true);
    expect(page.has('18 produits (40 formats) ont été ajoutés à votre catalogue, avec 31 stocks de départ')).toBe(true);
    expect([...page.root.querySelectorAll('a[href="/gestion/produits"]')].map((link) => link.textContent)).toContainEqual(expect.stringContaining('Voir les produits'));
  });

  it('keeps the same import identifier on a retry after a network failure', async () => {
    const api = fakeApi();
    api.import.mockRejectedValueOnce(new ApiError(0, 'network', ''));
    const page = await open(api);
    await page.pick();

    await page.click('Importer ces 18 produits');
    expect(page.root.querySelector('[role="alert"]')?.textContent).toContain('injoignable');
    expect(page.has('Importer ces 18 produits')).toBe(true);
    await page.click('Importer ces 18 produits');

    expect(api.import).toHaveBeenCalledTimes(2);
    expect(api.import.mock.calls[1][0]).toBe(api.import.mock.calls[0][0]);
    expect(page.has('L’importation est terminée.')).toBe(true);
  });

  it('says nothing was added twice when the server recognises the import', async () => {
    const api = fakeApi();
    api.import.mockResolvedValue(report({ canImport: false, newProducts: 0, newFormats: 0, alreadyProcessed: true, skippedCount: 3 }));
    const page = await open(api);
    await page.pick();

    await page.click('Importer ces 18 produits');

    expect(page.has('Cette importation avait déjà été faite')).toBe(true);
  });

  it('goes back to the corrections list when the server finds lines to correct at import time', async () => {
    const api = fakeApi();
    api.import.mockResolvedValue(report({ canImport: false, problemCount: 1, problems: [{ line: 9, column: 'Format', message: 'x', text: '' }] }));
    const page = await open(api);
    await page.pick();

    await page.click('Importer ces 18 produits');

    expect(page.has('1 ligne est à corriger')).toBe(true);
  });

  it('shows the server message when the file is refused, and lets the user choose another', async () => {
    const api = fakeApi();
    api.preview.mockRejectedValue(new ApiError(422, 'rule_violation', 'Ce fichier n’est pas un classeur Excel (.xlsx). Utilisez le modèle à télécharger.'));
    const page = await open(api);

    await page.pick('liste.csv');

    expect(page.root.querySelector('[role="alert"]')?.textContent).toContain('n’est pas un classeur Excel');
    expect(page.has('Choisir un fichier')).toBe(true);
  });

  it('lets the user start again after an import', async () => {
    const page = await open();
    await page.pick();
    await page.click('Importer ces 18 produits');

    await page.click('Importer un autre fichier');

    expect(page.has('1. Téléchargez le modèle')).toBe(true);
    expect(page.has('L’importation est terminée')).toBe(false);
  });

  it('warns when offline', async () => {
    const page = await open(fakeApi(), false);

    expect(page.has('Vous êtes hors ligne')).toBe(true);
  });
});
