import { fakeApi, fakeTeamApi, render } from '../management/test-support';
import { ApiError } from '../sync/sync-api';
import { AuditApi, AuditEntry, AuditPage } from './audit-api';
import { JournalPage } from './journal-page';

const NOW = new Date(2026, 9, 23, 10, 42);

const entry = (n: number, overrides: Partial<AuditEntry> = {}): AuditEntry => ({
  id: `e-${n}`, at: new Date(2026, 9, 23, 9, n).toISOString(), userId: 'u-moussa', action: 'sale.cancelled',
  resourceId: `r-${n}`, detail: `Erreur de saisie ${n}`, ...overrides,
});

const page = (entries: AuditEntry[], hasMore = false): AuditPage => ({ entries, hasMore });

function fakeAuditApi() {
  return { list: vi.fn().mockResolvedValue(page([])) };
}

async function open(options: { first?: AuditPage; audit?: ReturnType<typeof fakeAuditApi>; role?: string; online?: boolean; directory?: { id: string; displayName: string }[] | Error } = {}) {
  const audit = options.audit ?? fakeAuditApi();
  if (options.first) {
    audit.list.mockResolvedValue(options.first);
  }
  const team = fakeTeamApi();
  if (options.directory instanceof Error) {
    team.directory.mockRejectedValue(options.directory);
  } else {
    team.directory.mockResolvedValue(options.directory ?? [{ id: 'u-moussa', displayName: 'Moussa Traoré' }]);
  }
  const rendered = await render(JournalPage, {
    api: fakeApi(),
    teamApi: team,
    role: options.role ?? 'Owner',
    online: options.online,
    extraProviders: [{ provide: AuditApi, useValue: audit }],
  });
  return { page: rendered, audit, team };
}

describe('JournalPage', () => {
  beforeEach(() => {
    vi.useFakeTimers({ toFake: ['Date'], now: NOW });
  });
  afterEach(() => vi.useRealTimers());

  it('shows each entry with the action in French, the author by name and the detail (SC-M9)', async () => {
    const { page: view } = await open({
      first: page([
        entry(1, { action: 'sale.cancelled', detail: 'Erreur de saisie' }),
        entry(2, { action: 'sale.discount.refused', detail: 'Remise de 5 000 FCFA' }),
        entry(3, { action: 'stock.loss', detail: 'Savon : -2 (Périmé)' }),
      ]),
    });

    const rows = [...view.root.querySelectorAll('tbody tr')].map((row) => row.textContent!.replace(/\s+/g, ' '));
    expect(rows).toHaveLength(3);
    expect(rows[0]).toContain('Vente annulée');
    expect(rows[0]).toContain('Moussa Traoré');
    expect(rows[0]).toContain('Erreur de saisie');
    expect(rows[1]).toContain('Remise refusée');
    expect(rows[2]).toContain('Perte de stock');
    expect(view.has('sale.cancelled')).toBe(false);
  });

  it('asks for the last 7 days, all categories, 50 at a time, and shows the count', async () => {
    const { audit, page: view } = await open({ first: page([entry(1), entry(2)]) });

    const query = audit.list.mock.calls[0][0];
    expect(query.category).toBeNull();
    expect(query.limit).toBe(50);
    expect(query.offset).toBe(0);
    expect(query.range.from).toBe(new Date(2026, 9, 17).toISOString());
    expect(query.range.to).toBe(NOW.toISOString());
    expect(view.has('2 actions affichées')).toBe(true);
    expect(view.has('Fin du journal pour cette période.')).toBe(true);
  });

  it('falls back to a short identifier when the directory cannot be read', async () => {
    const { page: view } = await open({ first: page([entry(1, { userId: '0a1b2c3d-aaaa-bbbb-cccc-111122223333' })]), directory: new ApiError(403, 'forbidden', '') });
    expect(view.has('Utilisateur 0a1b2c3d')).toBe(true);
  });

  it('marks the refusals in words and by a second cue, not only a colour', async () => {
    const { page: view } = await open({ first: page([entry(1, { action: 'sale.cancel.refused' })]) });
    const cell = view.root.querySelector('tbody th')!;
    expect(cell.textContent).toContain('Annulation refusée');
    expect(cell.classList.contains('text-erreur')).toBe(true);
  });

  it('filters by category and restarts from the first page (SC-M7)', async () => {
    const { page: view, audit } = await open({ first: page([entry(1), entry(2)], true) });

    audit.list.mockResolvedValue(page([entry(9, { action: 'sale.discount.approved' })]));
    await view.click('Remises et annulations');

    const query = audit.list.mock.calls.at(-1)![0];
    expect(query.category).toBe('sales');
    expect(query.offset).toBe(0);
    expect(view.root.querySelectorAll('tbody tr')).toHaveLength(1);
    expect(view.root.querySelector('[aria-pressed="true"]')).toBeTruthy();
  });

  it('changes the period and asks again from the start', async () => {
    const { page: view, audit } = await open({ first: page([entry(1)]) });

    await view.click('30 jours');
    expect(audit.list.mock.calls.at(-1)![0].range.from).toBe(new Date(2026, 8, 24).toISOString());

    await view.click('Aujourd’hui');
    expect(audit.list.mock.calls.at(-1)![0].range.from).toBe(new Date(2026, 9, 23).toISOString());
  });

  it('loads the next page from where the list stops, appends without duplicates and stops when there is no more (SC-M6)', async () => {
    const firstPage = Array.from({ length: 50 }, (_, index) => entry(index));
    const { page: view, audit } = await open({ first: page(firstPage, true) });
    expect(view.has('Charger plus')).toBe(true);

    audit.list.mockResolvedValue(page(Array.from({ length: 20 }, (_, index) => entry(50 + index)), false));
    await view.click('Charger plus');

    expect(audit.list.mock.calls.at(-1)![0].offset).toBe(50);
    expect(view.root.querySelectorAll('tbody tr')).toHaveLength(70);
    expect(view.has('70 actions affichées')).toBe(true);
    expect(view.has('Charger plus')).toBe(false);
    expect(view.has('Fin du journal pour cette période.')).toBe(true);
  });

  it('ignores a late answer for a filter that was abandoned', async () => {
    const { page: view, audit } = await open({ first: page([entry(1)]) });
    let releaseOld!: (value: AuditPage) => void;
    audit.list.mockImplementationOnce(() => new Promise<AuditPage>((resolve) => (releaseOld = resolve)));
    const clickOld = view.click('30 jours'); // lecture lente, abandonnée juste après

    audit.list.mockResolvedValue(page([entry(7, { detail: 'Réponse récente' })]));
    await view.click('Aujourd’hui');
    releaseOld(page([entry(8, { detail: 'Réponse périmée' })]));
    await clickOld;

    expect(view.has('Réponse récente')).toBe(true);
    expect(view.has('Réponse périmée')).toBe(false);
  });

  it('explains a first load failure and recovers on retry', async () => {
    const audit = fakeAuditApi();
    audit.list.mockRejectedValueOnce(new ApiError(0, 'network', ''));
    const { page: view } = await open({ audit });
    expect(view.root.querySelector('[role="alert"]')?.textContent).toContain('injoignable');

    audit.list.mockResolvedValue(page([entry(1)]));
    await view.click('Réessayer');
    expect(view.root.querySelectorAll('tbody tr')).toHaveLength(1);
  });

  it('keeps the entries already shown when loading more fails, and says so', async () => {
    const { page: view, audit } = await open({ first: page([entry(1), entry(2)], true) });
    audit.list.mockRejectedValue(new ApiError(0, 'network', ''));

    await view.click('Charger plus');

    expect(view.root.querySelectorAll('tbody tr')).toHaveLength(2);
    expect(view.root.querySelector('[role="alert"]')?.textContent).toContain('injoignable');
  });

  it('says when nothing sensitive happened, and when it is offline', async () => {
    const { page: view } = await open({ first: page([]), online: false });
    expect(view.has('Aucune action sensible sur cette période.')).toBe(true);
    expect(view.root.querySelector('[role="note"]')?.textContent).toContain('hors ligne');
  });

  it('keeps the table inside a positioned scrolling container so hidden text cannot widen the page', async () => {
    const { page: view } = await open({ first: page([entry(1)]) });
    expect(view.root.querySelector('table')!.closest('div')!.classList.contains('relative')).toBe(true);
  });
});
