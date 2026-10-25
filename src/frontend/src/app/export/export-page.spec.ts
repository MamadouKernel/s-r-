import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { NetworkService } from '../services/network.service';
import { ApiError } from '../sync/sync-api';
import { ExportApi } from './export-api';
import { ExportPage } from './export-page';

async function open(online = true) {
  const download = vi.fn().mockResolvedValue({ blob: new Blob(['x']), fileName: 'soro-export-2026-10-25.xlsx' });
  TestBed.configureTestingModule({
    providers: [
      provideRouter([]),
      { provide: ExportApi, useValue: { download } },
      { provide: NetworkService, useValue: { online: signal(online) } },
    ],
  });
  const fixture = TestBed.createComponent(ExportPage);
  const root = fixture.nativeElement as HTMLElement;
  const settle = async () => {
    await fixture.whenStable();
    await new Promise((resolve) => setTimeout(resolve, 5));
    await fixture.whenStable();
  };
  await settle();
  return {
    root, download, settle,
    has: (text: string) => (root.textContent ?? '').replace(/\s+/g, ' ').includes(text),
    click: async (label: string) => {
      const button = [...root.querySelectorAll('button')].find((candidate) => (candidate.textContent ?? '').includes(label));
      expect(button, `bouton « ${label} » introuvable`).toBeTruthy();
      button!.click();
      await settle();
    },
    type: async (selector: string, value: string) => {
      const input = root.querySelector<HTMLInputElement>(selector)!;
      input.value = value;
      input.dispatchEvent(new Event('input'));
      await settle();
    },
  };
}

const today = () => new Date().toISOString().slice(0, 10);
const monthsAgo = (months: number) => {
  const date = new Date();
  date.setUTCMonth(date.getUTCMonth() - months);
  return date.toISOString().slice(0, 10);
};

describe('ExportPage', () => {
  beforeEach(() => {
    URL.createObjectURL = vi.fn(() => 'blob:export');
    URL.revokeObjectURL = vi.fn();
    vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => undefined);
  });

  afterEach(() => vi.restoreAllMocks());

  it('says the export is always available, lists what the file contains and warns about personal data', async () => {
    const page = await open();

    expect(page.has('Cet export reste toujours disponible')).toBe(true);
    expect(page.has('même si votre abonnement est terminé')).toBe(true);
    for (const content of ['Produits', 'Clients', 'Ventes', 'Versements', 'Mouvements de stock']) {
      expect(page.has(content)).toBe(true);
    }
    expect(page.has('Jamais dans le fichier : les codes PIN, les mots de passe, les appareils, les photos.')).toBe(true);
    expect(page.has('Gardez ce fichier en lieu sûr.')).toBe(true);
    expect(page.has('numéros de téléphone de vos clients et vos prix d’achat')).toBe(true);
  });

  it('proposes the twelve last months by default and downloads them', async () => {
    const page = await open();
    expect(page.has('Historique choisi : les 12 derniers mois')).toBe(true);

    await page.click('Télécharger l’export Excel');

    expect(page.download).toHaveBeenCalledWith(monthsAgo(12), today());
  });

  it.each([
    ['1 mois', 'le dernier mois', 1],
    ['3 mois', 'les 3 derniers mois', 3],
    ['Depuis le début', 'tout l’historique, depuis le début (5 ans au plus)', 60],
  ])('sends the period of « %s »', async (label, text, months) => {
    const page = await open();

    await page.click(label);
    expect(page.has(text)).toBe(true);
    await page.click('Télécharger l’export Excel');

    expect(page.download).toHaveBeenCalledWith(monthsAgo(months), today());
  });

  it('lets the owner choose the two dates, and the last day is sent as chosen', async () => {
    const page = await open();
    await page.click('Choisir les dates');
    await page.type('#du', '2026-10-01');
    await page.type('#au', '2026-10-25');
    expect(page.has('du 01/10/2026 au 25/10/2026')).toBe(true);

    await page.click('Télécharger l’export Excel');

    expect(page.download).toHaveBeenCalledWith('2026-10-01', '2026-10-25');
  });

  it('refuses a start after the end and a missing date without asking the server', async () => {
    const page = await open();
    await page.click('Choisir les dates');
    await page.type('#du', '2026-10-31');
    await page.type('#au', '2026-10-01');
    await page.click('Télécharger l’export Excel');
    expect(page.root.querySelector('[role="alert"]')?.textContent).toContain('Le début de la période doit être avant sa fin.');

    await page.type('#du', '');
    await page.click('Télécharger l’export Excel');
    expect(page.root.querySelector('[role="alert"]')?.textContent).toContain('Choisissez la date de début et la date de fin.');
    expect(page.download).not.toHaveBeenCalled();
  });

  it('saves the file under the name of the server and confirms it, then offers to download again', async () => {
    const page = await open();

    await page.click('Télécharger l’export Excel');

    expect(URL.createObjectURL).toHaveBeenCalled();
    expect(HTMLAnchorElement.prototype.click).toHaveBeenCalled();
    expect(page.has('Votre export est prêt.')).toBe(true);
    expect(page.has('soro-export-2026-10-25.xlsx')).toBe(true);
    expect(page.has('Gardez-le en lieu sûr')).toBe(true);
    expect(page.has('Télécharger de nouveau')).toBe(true);
  });

  it('turns the button off while the file is being prepared and says what is going on', async () => {
    const page = await open();
    let release: (value: { blob: Blob; fileName: string }) => void = () => undefined;
    page.download.mockReturnValue(new Promise((resolve) => (release = resolve)));

    const clicking = page.click('Télécharger l’export Excel');
    await vi.waitFor(async () => {
      await page.settle();
      expect(page.has('Préparation du fichier…')).toBe(true);
    });
    const button = [...page.root.querySelectorAll('button')].find((candidate) => (candidate.textContent ?? '').includes('Préparation'))!;
    expect(button.disabled).toBe(true);
    expect(page.has('Nous rassemblons vos données')).toBe(true);
    release({ blob: new Blob(['x']), fileName: 'soro-export-2026-10-25.xlsx' });
    await clicking;

    await vi.waitFor(async () => {
      await page.settle();
      expect(page.has('Votre export est prêt.')).toBe(true);
    });
  });

  it.each([
    ['too_many_requests', 429, 'Vous avez demandé plusieurs exports : réessayez dans 12 minutes.'],
    ['rule_violation', 422, 'Il y a trop de données pour cette période (143 210 lignes). Choisissez une période plus courte.'],
  ])('shows the server message in words when the export is refused (%s)', async (code, status, message) => {
    const page = await open();
    page.download.mockRejectedValue(new ApiError(status, code, message));

    await page.click('Télécharger l’export Excel');

    expect(page.root.querySelector('[role="alert"]')?.textContent).toContain(message);
    expect(page.has('Votre export est prêt.')).toBe(false);
  });

  it('says the server cannot be reached', async () => {
    const page = await open();
    page.download.mockRejectedValue(new ApiError(0, 'network', ''));

    await page.click('Télécharger l’export Excel');

    expect(page.root.querySelector('[role="alert"]')?.textContent).toContain('injoignable');
  });

  it('warns when offline', async () => {
    const page = await open(false);

    expect(page.has('Vous êtes hors ligne')).toBe(true);
  });
});
