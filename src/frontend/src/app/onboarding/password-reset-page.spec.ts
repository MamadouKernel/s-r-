import { ApiError } from '../sync/sync-api';
import { PasswordResetPage } from './password-reset-page';
import { fakeOnboardingApi, renderPage } from './test-support';

async function toCode(api = fakeOnboardingApi()) {
  const page = await renderPage(PasswordResetPage, { api });
  await page.type('#reinit-email', ' mamadou@exemple.ci ');
  await page.submit();
  return page;
}

describe('PasswordResetPage', () => {
  it('refuses an address that cannot be one before asking the server', async () => {
    const page = await renderPage(PasswordResetPage);
    await page.type('#reinit-email', 'abc');

    await page.submit();

    expect(page.api.startPasswordReset).not.toHaveBeenCalled();
    expect(page.alertText()).toContain('adresse e-mail n’est pas valide');
  });

  it('gives the same answer whether or not the address has an account (SC-U6)', async () => {
    const page = await toCode();

    expect(page.api.startPasswordReset).toHaveBeenCalledWith('mamadou@exemple.ci');
    expect(page.root.querySelector('[role="status"]')?.textContent).toContain('Si cette adresse e-mail a un compte');
    expect(page.has('Nouveau mot de passe')).toBe(true);
  });

  it('checks the code and the new password, then sets it and returns to the login with a message', async () => {
    const page = await toCode();

    await page.submit();
    expect(page.api.confirmPasswordReset).not.toHaveBeenCalled();
    expect(page.alertText()).toContain('6 chiffres');

    await page.type('#reinit-code', '123456');
    await page.type('#reinit-mdp', 'court');
    await page.submit();
    expect(page.alertText()).toContain('10 caractères');

    await page.type('#reinit-mdp', 'Nouveau-mot-2027');
    await page.submit();

    expect(page.api.confirmPasswordReset).toHaveBeenCalledWith('mamadou@exemple.ci', '123456', 'Nouveau-mot-2027');
    expect(page.flash.current()).toBe('Mot de passe changé. Connectez-vous avec le nouveau.');
    expect(page.navigate).toHaveBeenCalledWith('/connexion-proprietaire');
  });

  it('shows a wrong or expired code in plain words and keeps the form', async () => {
    const api = fakeOnboardingApi();
    api.confirmPasswordReset.mockRejectedValue(new ApiError(422, 'invalid_code', 'Code incorrect ou expiré. Demandez un nouveau code.'));
    const page = await toCode(api);
    await page.type('#reinit-code', '000000');
    await page.type('#reinit-mdp', 'Nouveau-mot-2027');

    await page.submit();

    expect(page.alertText()).toContain('Code incorrect ou expiré');
    expect(page.navigate).not.toHaveBeenCalled();
  });

  it('goes back to the address to ask for another code', async () => {
    const page = await toCode();
    await page.click('Changer d’adresse e-mail');
    expect(page.has('Recevoir un code')).toBe(true);
  });
});
