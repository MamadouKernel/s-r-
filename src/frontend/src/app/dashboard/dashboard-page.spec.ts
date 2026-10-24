import { TestBed } from '@angular/core/testing';
import { fakeApi, fakeTeamApi, render } from '../management/test-support';
import { ApiError } from '../sync/sync-api';
import { ActivityReport, DashboardApi, ProductRanking, SessionSummary } from './dashboard-api';
import { DashboardPage } from './dashboard-page';

const NOW = new Date(2026, 9, 23, 10, 42);
const NBSP = ' ';

const activity = (overrides: Partial<ActivityReport> = {}): ActivityReport => ({
  from: '', to: '', salesCount: 0, netTotalFcfa: 0, discountTotalFcfa: 0, cancelledCount: 0, cancelledTotalFcfa: 0,
  byPaymentMethod: [], byCashier: [], sessionsClosed: 0, shortfallFcfa: 0, surplusFcfa: 0, ...overrides,
});

const session = (overrides: Partial<SessionSummary> = {}): SessionSummary => ({
  id: 's-1', cashierId: 'c-awa', registerId: 'r', openedAt: new Date(2026, 9, 23, 8, 0).toISOString(), openingFloatFcfa: 10_000,
  cashReceivedFcfa: 3_000, expectedCashFcfa: 13_000, status: 'Open', countedCashFcfa: null, varianceFcfa: null, closedAt: null, ...overrides,
});

function fakeDashboardApi() {
  return {
    activity: vi.fn().mockResolvedValue(activity()),
    openSessions: vi.fn().mockResolvedValue([]),
    closedWithVariance: vi.fn().mockResolvedValue([]),
    topProducts: vi.fn().mockResolvedValue([]),
  };
}

async function open(options: {
  today?: ActivityReport;
  yesterday?: ActivityReport;
  open?: SessionSummary[];
  variances?: SessionSummary[];
  role?: string;
  online?: boolean;
  ranking?: ProductRanking[];
  dashboard?: ReturnType<typeof fakeDashboardApi>;
  products?: ReturnType<typeof fakeApi>;
  team?: ReturnType<typeof fakeTeamApi>;
} = {}) {
  const dashboard = options.dashboard ?? fakeDashboardApi();
  dashboard.activity.mockImplementation(async (range: { from: string; to: string }) =>
    new Date(range.from).getDate() === NOW.getDate() ? (options.today ?? activity()) : (options.yesterday ?? activity()),
  );
  dashboard.openSessions.mockResolvedValue(options.open ?? []);
  dashboard.closedWithVariance.mockResolvedValue(options.variances ?? []);
  // Un classement imposé écrase le comportement par défaut ; sans lui, un faux serveur fourni par le test garde le sien.
  if (options.ranking || !options.dashboard) {
    dashboard.topProducts.mockResolvedValue(options.ranking ?? []);
  }
  const products = options.products ?? fakeApi();
  const team = options.team ?? fakeTeamApi();
  team.directory.mockResolvedValue([{ id: 'c-awa', displayName: 'Awa Koné' }, { id: 'c-issa', displayName: 'Issa Diallo' }]);
  const page = await render(DashboardPage, {
    api: products,
    teamApi: team,
    role: options.role ?? 'Owner',
    online: options.online,
    extraProviders: [{ provide: DashboardApi, useValue: dashboard }],
  });
  return { page, dashboard, products, team };
}

describe('DashboardPage', () => {
  beforeEach(() => {
    vi.useFakeTimers({ toFake: ['Date', 'setInterval', 'clearInterval'], now: NOW });
  });
  afterEach(() => {
    vi.useRealTimers();
    TestBed.resetTestingModule();
  });

  it('shows the turnover, the tickets, the average basket and the payment split (SC-D1)', async () => {
    const { page } = await open({
      today: activity({
        salesCount: 3, netTotalFcfa: 9_000, discountTotalFcfa: 500, cancelledCount: 1, cancelledTotalFcfa: 2_000,
        byPaymentMethod: [
          { method: 'Cash', amountFcfa: 2_000, count: 2 },
          { method: 'OrangeMoney', amountFcfa: 6_000, count: 1 },
          { method: 'Wave', amountFcfa: 1_000, count: 1 },
        ],
      }),
    });

    expect(page.has(`9${NBSP}000${NBSP}FCFA`)).toBe(true);
    expect(page.has(`3${NBSP}000${NBSP}FCFA`)).toBe(true); // panier moyen : 9 000 / 3
    const rows = [...page.root.querySelectorAll('#paiements-titre ~ ul li')].map((li) => li.textContent?.replace(/\s+/g, ' ').trim());
    expect(rows[0]).toContain('Orange Money');
    expect(rows[0]).toContain('67 %');
    expect(rows[1]).toContain('Espèces');
    expect(rows[1]).toContain('22 %');
    expect(page.has(`1 · 2${NBSP}000${NBSP}FCFA`)).toBe(true); // ventes annulées : nombre · montant
    expect(page.has(`500${NBSP}FCFA`)).toBe(true); // remises accordées
  });

  it('says the day has no sale yet, with a dash for the average basket', async () => {
    const { page } = await open();
    expect(page.has('Aucune vente pour le moment aujourd’hui.')).toBe(true);
    expect(page.root.querySelector('dl')!.textContent).toContain('—');
    expect(page.has('Aucun paiement enregistré aujourd’hui.')).toBe(true);
  });

  it.each([
    [1_120, 1_000, 'en hausse de 12 %'],
    [920, 1_000, 'en baisse de 8 %'],
    [1_000, 1_000, 'stable'],
    [500, 0, 'pas de vente hier à la même heure pour comparer'],
  ])('compares to yesterday at the same time in words: %i vs %i (SC-D2)', async (current, previous, expected) => {
    const { page } = await open({ today: activity({ netTotalFcfa: current, salesCount: 1 }), yesterday: activity({ netTotalFcfa: previous, salesCount: 1 }) });
    expect(page.root.textContent).toContain(expected);
  });

  it('reads today and yesterday with ranges that end at the same elapsed time', async () => {
    const { dashboard } = await open();
    const [today, yesterday] = dashboard.activity.mock.calls.map((call) => call[0]);
    expect(today.from).toBe(new Date(2026, 9, 23).toISOString());
    expect(today.to).toBe(NOW.toISOString());
    expect(yesterday.from).toBe(new Date(2026, 9, 22).toISOString());
    expect(yesterday.to).toBe(new Date(2026, 9, 22, 10, 42).toISOString());
  });

  it('names the cashiers from the directory, and falls back to a short identifier', async () => {
    const { page } = await open({
      today: activity({
        salesCount: 4, netTotalFcfa: 7_000,
        byCashier: [{ cashierId: 'c-awa', salesCount: 3, netTotalFcfa: 6_000 }, { cashierId: '0a1b2c3d-unknown', salesCount: 1, netTotalFcfa: 1_000 }],
      }),
    });
    expect(page.has('Awa Koné')).toBe(true);
    expect(page.has('Caissier 0a1b2c3d')).toBe(true);
  });

  it('lists open registers and flags one open for more than 12 hours (SC-D3)', async () => {
    const { page } = await open({
      open: [
        session({ id: 'a', cashierId: 'c-awa', openedAt: new Date(2026, 9, 23, 8, 0).toISOString() }),
        session({ id: 'b', cashierId: 'c-issa', openedAt: new Date(2026, 9, 22, 20, 0).toISOString() }),
      ],
    });
    expect(page.has('Awa Koné')).toBe(true);
    expect(page.has('Issa Diallo')).toBe(true);
    const warnings = page.root.querySelectorAll('li p.text-avertissement');
    expect(warnings).toHaveLength(1);
    expect(warnings[0].closest('li')!.textContent).toContain('Issa Diallo');
    expect(warnings[0].textContent).toContain('plus de 12 heures');
  });

  it('shows today’s closing gaps in words and keeps only the registers closed today (SC-D3)', async () => {
    const { page } = await open({
      today: activity({ shortfallFcfa: 500, surplusFcfa: 0, sessionsClosed: 1 }),
      variances: [
        session({ id: 'x', cashierId: 'c-awa', status: 'Closed', varianceFcfa: -500, closedAt: new Date(2026, 9, 23, 7, 30).toISOString() }),
        session({ id: 'old', cashierId: 'c-issa', status: 'Closed', varianceFcfa: -9_999, closedAt: new Date(2026, 9, 22, 22, 0).toISOString() }),
      ],
    });
    expect(page.has(`Manque de 500${NBSP}FCFA`)).toBe(true);
    expect(page.root.textContent).not.toContain(`9${NBSP}999`); // clôturée hier soir : pas dans les écarts d'aujourd'hui
    expect(page.root.textContent).not.toContain('Issa Diallo');
    // \s recouvre l'espace insécable : on normalise donc aussi le texte attendu.
    const flat = (text: string | null | undefined) => (text ?? '').replace(/\s+/g, ' ');
    expect(flat(page.root.textContent)).toContain(flat(`Cumul du jour : 500${NBSP}FCFA de manques, 0${NBSP}FCFA d’excédents.`));
  });

  it('says there is no gap and no open register when everything is quiet', async () => {
    const { page } = await open();
    expect(page.has('Aucune caisse ouverte.')).toBe(true);
    expect(page.has('Aucun écart de caisse aujourd’hui.')).toBe(true);
  });

  it('counts the products at their alert threshold and links to the products', async () => {
    const products = fakeApi();
    products.lowStock.mockResolvedValue([{ id: 'a' }, { id: 'b' }]);
    const { page } = await open({ products });
    expect(page.has('2 produits à réapprovisionner')).toBe(true);
    expect(page.root.querySelector('a[href="/gestion/produits"]')).toBeTruthy();
  });

  it('says the stock is correct when nothing is under its threshold', async () => {
    const { page } = await open();
    expect(page.has('le stock est correct')).toBe(true);
  });

  it('hides the stock block from the accountant and never asks for the alerts (SC-D5)', async () => {
    const products = fakeApi();
    const { page } = await open({ role: 'Accountant', products });
    expect(page.root.querySelector('#stock-titre')).toBeNull();
    expect(products.lowStock).not.toHaveBeenCalled();
  });

  it('keeps the other blocks when one fails, and says which (SC-D6)', async () => {
    const products = fakeApi();
    products.lowStock.mockRejectedValue(new ApiError(500, 'internal_error', ''));
    const { page } = await open({ today: activity({ salesCount: 1, netTotalFcfa: 1_000 }), products });
    expect(page.has(`1${NBSP}000${NBSP}FCFA`)).toBe(true);
    expect(page.root.querySelector('#stock-titre')!.closest('section')!.querySelector('[role="alert"]')).toBeTruthy();
  });

  it('keeps the last figures with their time and a warning when a refresh fails (SC-D4)', async () => {
    const { page, dashboard } = await open({ today: activity({ salesCount: 2, netTotalFcfa: 5_000 }) });
    expect(page.has('Mis à jour à 10:42')).toBe(true);

    vi.setSystemTime(new Date(2026, 9, 23, 10, 50));
    dashboard.activity.mockRejectedValue(new ApiError(0, 'network', ''));
    await page.click('Actualiser');

    expect(page.has(`5${NBSP}000${NBSP}FCFA`)).toBe(true);
    expect(page.has('Mis à jour à 10:42')).toBe(true);
    expect(page.root.querySelector('[role="alert"]')?.textContent).toContain('L’actualisation a échoué (chiffres du jour)');
    expect(page.root.querySelector('[role="alert"]')?.textContent).toContain('à 10:42');
  });

  it('clears the warning once a refresh succeeds again', async () => {
    const { page, dashboard } = await open({ today: activity({ salesCount: 2, netTotalFcfa: 5_000 }) });
    dashboard.activity.mockRejectedValue(new ApiError(0, 'network', ''));
    await page.click('Actualiser');
    expect(page.has('L’actualisation a échoué')).toBe(true);

    dashboard.activity.mockResolvedValue(activity({ salesCount: 3, netTotalFcfa: 7_000 }));
    await page.click('Actualiser');

    expect(page.has('L’actualisation a échoué')).toBe(false);
    expect(page.has(`7${NBSP}000${NBSP}FCFA`)).toBe(true);
  });

  it('explains a first load failure in the block itself and lets the user retry', async () => {
    const dashboard = fakeDashboardApi();
    dashboard.activity.mockRejectedValue(new ApiError(0, 'network', ''));
    const page = await render(DashboardPage, {
      api: fakeApi(),
      role: 'Owner',
      extraProviders: [{ provide: DashboardApi, useValue: dashboard }],
    });
    const block = page.root.querySelector('#ca-titre')!.closest('section')!;
    expect(block.querySelector('[role="alert"]')?.textContent).toContain('injoignable');

    dashboard.activity.mockResolvedValue(activity({ salesCount: 1, netTotalFcfa: 1_500 }));
    await page.click('Réessayer');

    expect(page.has(`1${NBSP}500${NBSP}FCFA`)).toBe(true);
    expect(page.root.querySelector('#ca-titre')!.closest('section')!.querySelector('[role="alert"]')).toBeNull();
  });

  it('says when it is offline', async () => {
    const { page } = await open({ online: false });
    expect(page.root.querySelector('[role="note"]')?.textContent).toContain('hors ligne');
  });

  it('refreshes by itself every minute while the page is visible and online (SC-D7)', async () => {
    const { dashboard } = await open();
    const before = dashboard.activity.mock.calls.length;

    await vi.advanceTimersByTimeAsync(60_000);

    expect(dashboard.activity.mock.calls.length).toBe(before + 2); // aujourd'hui et hier
  });

  it('does not refresh by itself while the page is hidden, and stops when it is left (SC-D7)', async () => {
    const { dashboard, page } = await open();
    const before = dashboard.activity.mock.calls.length;
    Object.defineProperty(document, 'visibilityState', { value: 'hidden', configurable: true });
    await vi.advanceTimersByTimeAsync(180_000);
    expect(dashboard.activity.mock.calls.length).toBe(before);

    Object.defineProperty(document, 'visibilityState', { value: 'visible', configurable: true });
    page.fixture.destroy();
    await vi.advanceTimersByTimeAsync(180_000);
    expect(dashboard.activity.mock.calls.length).toBe(before);
  });

  it('never runs two reads at the same time: a click during a read joins it (SC-D7)', async () => {
    const { page, dashboard } = await open();
    let release!: () => void;
    const gate = new Promise<void>((resolve) => (release = resolve));
    dashboard.activity.mockImplementation(async () => {
      await gate;
      return activity();
    });
    const before = dashboard.activity.mock.calls.length;

    const first = page.click('Actualiser');
    const second = page.click('Actualiser');
    release();
    await Promise.all([first, second]);

    expect(dashboard.activity.mock.calls.length - before).toBe(2); // une seule lecture : aujourd'hui + hier
  });

  it('announces the update discreetly and keeps the table in a positioned scrolling container', async () => {
    const { page } = await open({
      today: activity({ salesCount: 1, netTotalFcfa: 1_000, byCashier: [{ cashierId: 'c-awa', salesCount: 1, netTotalFcfa: 1_000 }] }),
    });
    expect(page.root.querySelector('p.sr-only[role="status"]')?.textContent).toContain('Données actualisées à 10:42.');
    expect(page.root.querySelector('table')!.closest('div')!.classList.contains('relative')).toBe(true);
  });

  describe('best sellers', () => {
    const item = (name: string, quantity: number, revenueFcfa: number, marginFcfa: number | null): ProductRanking => ({
      productId: name, name, quantity, revenueFcfa, marginFcfa, marginIsEstimate: false,
    });
    const ranking = [item('Savon', 30, 7_500, 4_500), item('Jus', 10, 6_000, 2_000), item('Fantôme', 2, 200, null)];

    it('shows the ranking of the last 7 days by quantity by default, with quantity first and revenue below (SC-M1)', async () => {
      const { page, dashboard } = await open({ ranking });
      const [range, by, limit] = dashboard.topProducts.mock.calls[0];
      expect(by).toBe('quantity');
      expect(limit).toBe(5);
      expect(range.from).toBe(new Date(2026, 9, 17).toISOString());
      expect(range.to).toBe(NOW.toISOString());

      const rows = [...page.root.querySelectorAll('#classement-titre ~ ol li')].map((li) => li.textContent?.replace(/\s+/g, ' ').trim());
      expect(rows[0]).toContain('1.');
      expect(rows[0]).toContain('Savon');
      expect(rows[0]).toContain('30 vendus');
      expect(rows[0].replace(/\u00a0/g, ' ')).toContain('7 500 FCFA de chiffre d’affaires');
      expect(rows[2]).toContain('Fantôme');
    });

    it('reloads only the ranking when the criterion or the period changes (SC-M1)', async () => {
      const { page, dashboard } = await open({ ranking });
      const activityCalls = dashboard.activity.mock.calls.length;

      await page.click('Plus de chiffre d’affaires');
      expect(dashboard.topProducts.mock.calls.at(-1)![1]).toBe('revenue');

      await page.click('30 jours');
      const [range, by] = dashboard.topProducts.mock.calls.at(-1)!;
      expect(by).toBe('revenue');
      expect(range.from).toBe(new Date(2026, 8, 24).toISOString());

      await page.click('Aujourd’hui');
      expect(dashboard.topProducts.mock.calls.at(-1)![0].from).toBe(new Date(2026, 9, 23).toISOString());
      expect(dashboard.activity.mock.calls.length).toBe(activityCalls); // le reste n'est pas relu
    });

    it('puts the revenue first when ranking by revenue, and names the margin as an estimate when ranking by margin', async () => {
      const { page } = await open({ ranking });
      await page.click('Plus de chiffre d’affaires');
      const firstRevenueRow = page.root.querySelector('#classement-titre ~ ol li')!.textContent!.replace(/\s+/g, ' ');
      expect(firstRevenueRow.replace(/\u00a0/g, ' ')).toContain('7 500 FCFA');
      expect(firstRevenueRow).toContain('30 vendus');

      await page.click('Plus rentables');
      const rows = [...page.root.querySelectorAll('#classement-titre ~ ol li')].map((li) => li.textContent!.replace(/\s+/g, ' ').replace(/\u00a0/g, ' '));
      expect(rows[0]).toContain('4 500 FCFA');
      expect(rows[2]).toContain('marge inconnue'); // SC-M4 : jamais une fausse marge
      expect(page.has('ce que le produit vous avait coûté')).toBe(true);
      expect(page.has('s’affiche « estimée »')).toBe(true);
    });

    it('marks a margin as estimated when some sales have no kept cost, and says nothing for an exact one (CM-03)', async () => {
      const mixed = [
        { ...item('Jus', 10, 6_000, 2_000), marginIsEstimate: true },
        item('Savon', 30, 7_500, 4_500),
        { ...item('Fantôme', 2, 200, null), marginIsEstimate: true },
      ];
      const { page } = await open({ ranking: mixed });
      await page.click('Plus rentables');

      const rows = [...page.root.querySelectorAll('#classement-titre ~ ol li')].map((li) => li.textContent!.replace(/\s+/g, ' ').replace(/\u00a0/g, ' '));
      expect(rows[0]).toContain('2 000 FCFA (estimée)');
      expect(rows[1]).toContain('4 500 FCFA');
      expect(rows[1]).not.toContain('estimée');
      expect(rows[2]).toContain('marge inconnue');
      expect(rows[2]).not.toContain('estimée');
    });

    it('marks the chosen criterion and period for assistive technologies, not only by colour', async () => {
      const { page } = await open({ ranking });
      const pressed = (label: string) =>
        [...page.root.querySelectorAll('#classement-titre ~ div button')].find((button) => button.textContent?.includes(label))!.getAttribute('aria-pressed');
      expect(pressed('Plus vendus')).toBe('true');
      expect(pressed('Plus rentables')).toBe('false');
      expect(pressed('7 jours')).toBe('true');
      expect(pressed('30 jours')).toBe('false');
    });

    it('does not offer the margin to the accountant (SC-M3)', async () => {
      const { page } = await open({ role: 'Accountant', ranking });
      expect(page.has('Plus rentables')).toBe(false);
      expect(page.has('Plus vendus')).toBe(true);
      expect(page.has('Plus de chiffre d’affaires')).toBe(true);
    });

    it('says when there is no sale on the period', async () => {
      const { page } = await open({ ranking: [] });
      expect(page.has('Aucune vente sur cette période.')).toBe(true);
    });

    it('keeps the other blocks when the ranking fails, and says so in the block (SC-D6)', async () => {
      const dashboard = fakeDashboardApi();
      dashboard.topProducts.mockRejectedValue(new ApiError(500, 'internal_error', ''));
      const { page } = await open({ today: activity({ salesCount: 1, netTotalFcfa: 1_000 }), dashboard });
      expect(page.has(`1${NBSP}000${NBSP}FCFA`)).toBe(true);
      expect(page.root.querySelector('#classement-titre')!.closest('section')!.querySelector('[role="alert"]')).toBeTruthy();
    });

    it('keeps the last ranking and warns when a refresh of it fails', async () => {
      const { page, dashboard } = await open({ ranking });
      dashboard.topProducts.mockRejectedValue(new ApiError(0, 'network', ''));
      await page.click('Actualiser');
      expect(page.has('Savon')).toBe(true);
      expect(page.root.querySelector('[role="alert"]')?.textContent).toContain('meilleures ventes');
    });
  });
});
