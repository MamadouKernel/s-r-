import { ApiError } from '../sync/sync-api';
import { MemberFormPage } from './member-form-page';
import { fakeApi, fakeTeamApi, render } from './test-support';

async function open(assignable = ['Cashier', 'StockKeeper']) {
  const teamApi = fakeTeamApi();
  teamApi.team.mockResolvedValue({ users: [], assignableRoles: assignable });
  const page = await render(MemberFormPage, { api: fakeApi(), teamApi, role: 'Manager' });
  return { page, teamApi };
}

async function fill(page: Awaited<ReturnType<typeof render>>, overrides: Record<string, string> = {}) {
  const values: Record<string, string> = {
    '#nom-membre': ' Awa Koné ', '#identifiant-membre': ' Awa.Kone ', '#pin-membre': '4321', '#pin-confirmation': '4321', ...overrides,
  };
  for (const [selector, value] of Object.entries(values)) {
    await page.type(selector, value);
  }
  const select = page.root.querySelector<HTMLSelectElement>('#profil-membre')!;
  select.value = 'Cashier';
  select.dispatchEvent(new Event('change'));
  await page.settle();
}

describe('MemberFormPage', () => {
  it('offers only the profiles the server says the actor may create (SC-E2)', async () => {
    const { page } = await open(['Cashier', 'StockKeeper']);
    const options = [...page.root.querySelectorAll('#profil-membre option')].map((option) => option.textContent?.trim());
    expect(options).toEqual(['Choisir un profil', 'Caissier', 'Magasinier']);
  });

  it('offers the manager profile too when the server lists it for the owner', async () => {
    const { page } = await open(['Manager', 'Cashier', 'StockKeeper', 'Accountant']);
    const options = [...page.root.querySelectorAll('#profil-membre option')].map((option) => option.textContent?.trim());
    expect(options).toContain('Gérant');
    expect(options).toContain('Comptable');
  });

  it('creates the member with a normalised identifier, goes back to the team and confirms', async () => {
    const { page, teamApi } = await open();
    await fill(page);

    await page.submit();

    expect(teamApi.create).toHaveBeenCalledTimes(1);
    const [id, input] = teamApi.create.mock.calls[0];
    expect(id).toMatch(/^[0-9a-f-]{36}$/);
    expect(input).toEqual({ identifier: 'awa.kone', displayName: 'Awa Koné', role: 'Cashier', pin: '4321' });
    expect(page.navigate).toHaveBeenCalledWith('/gestion/equipe');
    expect(page.flash.current()).toBe('Membre ajouté : Awa Koné.');
  });

  it('clears both PIN fields as soon as it sends, whatever the answer', async () => {
    const { page, teamApi } = await open();
    teamApi.create.mockRejectedValue(new ApiError(0, 'network', ''));
    await fill(page);

    await page.submit();

    expect(page.root.querySelector<HTMLInputElement>('#pin-membre')!.value).toBe('');
    expect(page.root.querySelector<HTMLInputElement>('#pin-confirmation')!.value).toBe('');
    expect(page.root.querySelector('[role="alert"]')?.textContent).toContain('injoignable');
  });

  it('never keeps the PIN in the page text', async () => {
    const { page } = await open();
    await fill(page);
    expect(page.root.textContent).not.toContain('4321');
  });

  it('explains each invalid field under the field and sends nothing', async () => {
    const { page, teamApi } = await open();
    await page.type('#identifiant-membre', 'a!');
    await page.type('#pin-membre', '12');

    await page.submit();

    expect(teamApi.create).not.toHaveBeenCalled();
    expect(page.root.querySelector('#nom-membre-erreur')?.textContent).toContain('obligatoire');
    expect(page.root.querySelector('#identifiant-erreur')?.textContent).toContain('3 à 32 caractères');
    expect(page.root.querySelector('#profil-erreur')?.textContent).toContain('Choisissez un profil');
    expect(page.root.querySelector('#pin-erreur')?.textContent).toContain('4 à 6 chiffres');
  });

  it('refuses two different PINs and a PIN with letters', async () => {
    const { page, teamApi } = await open();
    await fill(page, { '#pin-confirmation': '1111' });
    await page.submit();
    expect(page.root.querySelector('#pin-confirmation-erreur')?.textContent).toContain('pas identiques');

    await fill(page, { '#pin-membre': '12ab', '#pin-confirmation': '12ab' });
    await page.submit();
    expect(page.root.querySelector('#pin-erreur')?.textContent).toContain('4 à 6 chiffres');
    expect(teamApi.create).not.toHaveBeenCalled();
  });

  it('shows a taken identifier under its field and keeps the other fields (SC-E4)', async () => {
    const { page, teamApi } = await open();
    teamApi.create.mockRejectedValue(new ApiError(409, 'identifier_taken', 'Cet identifiant est déjà utilisé dans ce commerce.'));
    await fill(page);

    await page.submit();

    expect(page.root.querySelector('#identifiant-erreur')?.textContent).toContain('déjà utilisé');
    expect(page.navigate).not.toHaveBeenCalled();
    expect(page.root.querySelector<HTMLInputElement>('#nom-membre')!.value).toBe(' Awa Koné ');
  });

  it('keeps the same user id on a retry after a network failure, so nothing is created twice (SC-E8)', async () => {
    const { page, teamApi } = await open();
    teamApi.create.mockRejectedValueOnce(new ApiError(0, 'network', ''));
    await fill(page);
    await page.submit();

    await fill(page);
    await page.submit();

    expect(teamApi.create).toHaveBeenCalledTimes(2);
    expect(teamApi.create.mock.calls[1][0]).toBe(teamApi.create.mock.calls[0][0]);
  });

  it('says why when the profiles cannot be loaded', async () => {
    const teamApi = fakeTeamApi();
    teamApi.team.mockRejectedValue(new ApiError(403, 'forbidden', ''));
    const page = await render(MemberFormPage, { api: fakeApi(), teamApi, role: 'Cashier' });
    expect(page.root.querySelector('[role="alert"]')?.textContent).toContain('pas le droit');
  });
});
