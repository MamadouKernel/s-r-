import { ApiError } from '../sync/sync-api';
import { OwnerLoginPage } from './owner-login-page';
import { fakeOnboardingApi, ownerSession, renderPage } from './test-support';

describe('OwnerLoginPage', () => {
  it('asks for an address and a password, with links to the other paths', async () => {
    const page = await renderPage(OwnerLoginPage);

    expect(page.has('Votre adresse e-mail')).toBe(true);
    expect(page.has('Mot de passe')).toBe(true);
    expect(page.root.querySelector('a[href="/mot-de-passe-oublie"]')).toBeTruthy();
    expect(page.root.querySelector('a[href="/inscription"]')).toBeTruthy();
  });

  it('explains what is missing instead of disabling the button', async () => {
    const page = await renderPage(OwnerLoginPage);

    await page.submit();

    expect(page.api.ownerLogin).not.toHaveBeenCalled();
    expect(page.alertText()).toContain('Saisissez votre adresse e-mail et votre mot de passe');
    expect(page.root.querySelector<HTMLButtonElement>('button[type="submit"]')!.disabled).toBe(false);
  });

  it('logs the owner in and lands on the dashboard', async () => {
    const page = await renderPage(OwnerLoginPage);
    await page.type('#email-proprietaire', ' mamadou@exemple.ci ');
    await page.type('#mdp-proprietaire', 'Boutique-2026-ok');

    await page.submit();

    expect(page.api.ownerLogin).toHaveBeenCalledWith('mamadou@exemple.ci', 'Boutique-2026-ok');
    expect(page.auth.acceptOwnerSession).toHaveBeenCalledWith(ownerSession());
    expect(page.navigate).toHaveBeenCalledWith('/tableau-de-bord');
  });

  it('shows the same plain message for a wrong password and empties the password', async () => {
    const api = fakeOnboardingApi();
    api.ownerLogin.mockRejectedValue(new ApiError(401, 'invalid_credentials', 'Adresse e-mail ou mot de passe incorrect.'));
    const page = await renderPage(OwnerLoginPage, { api });
    await page.type('#email-proprietaire', 'mamadou@exemple.ci');
    await page.type('#mdp-proprietaire', 'Mauvais-mot-2026');

    await page.submit();

    expect(page.alertText()).toContain('Adresse e-mail ou mot de passe incorrect.');
    expect(page.field('#mdp-proprietaire').value).toBe('');
    expect(page.navigate).not.toHaveBeenCalled();
  });

  it('tells a locked owner to wait, in the words of the server', async () => {
    const api = fakeOnboardingApi();
    api.ownerLogin.mockRejectedValue(new ApiError(423, 'account_locked', 'Compte verrouillé temporairement. Réessayez dans quelques minutes.'));
    const page = await renderPage(OwnerLoginPage, { api });
    await page.type('#email-proprietaire', 'mamadou@exemple.ci');
    await page.type('#mdp-proprietaire', 'x');

    await page.submit();

    expect(page.alertText()).toContain('Réessayez dans quelques minutes');
  });

  it('shows the confirmation left by the password reset once', async () => {
    const page = await renderPage(OwnerLoginPage, { flash: 'Mot de passe changé. Connectez-vous avec le nouveau.' });

    expect(page.root.querySelector('[role="status"]')?.textContent).toContain('Mot de passe changé');
    expect(page.flash.current()).toBe('');
  });

  it('lets the owner see the password', async () => {
    const page = await renderPage(OwnerLoginPage);
    expect(page.field('#mdp-proprietaire').type).toBe('password');
    await page.check('input[type="checkbox"]', true);
    expect(page.field('#mdp-proprietaire').type).toBe('text');
  });
});
