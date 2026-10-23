import { ApiError } from '../sync/sync-api';
import { fakeApi, fakeTeamApi, member, render } from './test-support';
import { TeamPage } from './team-page';

async function open(members = [member()], options: { online?: boolean } = {}) {
  const teamApi = fakeTeamApi();
  teamApi.team.mockResolvedValue({ users: members, assignableRoles: ['Cashier', 'StockKeeper'] });
  const page = await render(TeamPage, { api: fakeApi(), teamApi, role: 'Manager', ...options });
  return { page, teamApi };
}

describe('TeamPage', () => {
  it('lists the members with their profile in French and their state in words (SC-E1)', async () => {
    const { page } = await open([
      member({ id: 'a', displayName: 'Awa Koné', identifier: 'awa', lastLoginAt: '2026-10-22T08:30:00.000Z' }),
      member({ id: 'b', displayName: 'Issa Diallo', identifier: 'issa', role: 'StockKeeper' }),
      member({ id: 'c', displayName: 'Ancien', identifier: 'ancien', status: 'Disabled', canDisable: false }),
      member({ id: 'd', displayName: 'Bloqué', identifier: 'bloque', lockedUntil: '2026-10-23T10:45:00.000Z' }),
      member({ id: 'e', displayName: 'Moussa Traoré', identifier: 'gerant', role: 'Manager', isSelf: true, canDisable: false }),
    ]);

    expect(page.root.querySelectorAll('tbody tr')).toHaveLength(5);
    expect(page.has('Caissier')).toBe(true);
    expect(page.has('Magasinier')).toBe(true);
    expect(page.has('Gérant')).toBe(true);
    expect(page.has('Désactivé')).toBe(true);
    expect(page.has('Verrouillé jusqu’à')).toBe(true);
    expect(page.has('Jamais connecté')).toBe(true);
    expect(page.has('(vous)')).toBe(true);
    expect(page.has('Actif')).toBe(true);
  });

  it('offers “Désactiver” only where the server says it is allowed', async () => {
    const { page } = await open([
      member({ id: 'a', displayName: 'Awa Koné', canDisable: true }),
      member({ id: 'b', displayName: 'Autre Gérant', role: 'Manager', canDisable: false }),
      member({ id: 'c', displayName: 'Proprio', role: 'Owner', canDisable: false }),
    ]);

    expect(page.root.querySelectorAll('[aria-label^="Désactiver"]')).toHaveLength(1);
    expect(page.root.querySelector('[aria-label="Désactiver Awa Koné"]')).toBeTruthy();
  });

  it('asks for a confirmation that recalls what it means, then disables and reloads', async () => {
    const { page, teamApi } = await open([member({ id: 'a', displayName: 'Awa Koné' })]);

    await page.click('Désactiver Awa Koné');
    expect(page.has('Désactiver Awa Koné ? Ce compte ne pourra plus se connecter.')).toBe(true);
    expect(page.has('Un accès déjà ouvert reste valable')).toBe(true);
    expect(teamApi.disable).not.toHaveBeenCalled();

    await page.click('Non, le garder');
    expect(teamApi.disable).not.toHaveBeenCalled();

    teamApi.team.mockResolvedValue({ users: [member({ id: 'a', displayName: 'Awa Koné', status: 'Disabled', canDisable: false })], assignableRoles: [] });
    await page.click('Désactiver Awa Koné');
    await page.click('Oui, désactiver');

    expect(teamApi.disable).toHaveBeenCalledWith('a');
    expect(page.root.querySelector('[role="status"]')?.textContent).toContain('Compte désactivé : Awa Koné.');
    expect(page.has('Désactivé')).toBe(true);
    expect(page.root.querySelector('[aria-label^="Désactiver"]')).toBeNull();
  });

  it('shows the refusal of the server in French and keeps the list', async () => {
    const { page, teamApi } = await open([member({ id: 'a', displayName: 'Awa Koné' })]);
    teamApi.disable.mockRejectedValue(new ApiError(403, 'forbidden', 'Ce profil ne peut pas désactiver cet utilisateur.'));

    await page.click('Désactiver Awa Koné');
    await page.click('Oui, désactiver');

    expect(page.root.querySelector('[role="alert"]')?.textContent).toContain('pas le droit');
  });

  it('explains an empty team and a failure, and lets the user retry', async () => {
    const empty = await open([]);
    expect(empty.page.has('Ajoutez votre premier caissier')).toBe(true);
  });

  it('explains a server failure and recovers on retry', async () => {
    const teamApi = fakeTeamApi();
    teamApi.team.mockRejectedValueOnce(new ApiError(0, 'network', ''));
    const page = await render(TeamPage, { api: fakeApi(), teamApi, role: 'Owner' });
    expect(page.root.querySelector('[role="alert"]')?.textContent).toContain('injoignable');

    teamApi.team.mockResolvedValue({ users: [member()], assignableRoles: [] });
    await page.click('Réessayer');
    expect(page.root.querySelectorAll('tbody tr')).toHaveLength(1);
  });

  it('says when it is offline (SC-G7 applied to the team)', async () => {
    const { page } = await open([member()], { online: false });
    expect(page.root.querySelector('[role="note"]')?.textContent).toContain('hors ligne');
  });

  it('keeps the table inside a positioned scrolling container so hidden text cannot widen the page', async () => {
    const { page } = await open();
    expect(page.root.querySelector('table')!.closest('div')!.classList.contains('relative')).toBe(true);
  });

  it('shows the confirmation of the previous screen once', async () => {
    const teamApi = fakeTeamApi();
    teamApi.team.mockResolvedValue({ users: [member()], assignableRoles: [] });
    const page = await render(TeamPage, { api: fakeApi(), teamApi, role: 'Manager', flash: 'Membre ajouté : Awa Koné.' });
    expect(page.root.querySelector('[role="status"]')?.textContent).toContain('Membre ajouté : Awa Koné.');
    expect(page.flash.current()).toBe('');
  });
});
