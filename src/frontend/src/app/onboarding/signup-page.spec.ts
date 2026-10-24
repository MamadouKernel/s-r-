import { ApiError } from '../sync/sync-api';
import { SignupPage } from './signup-page';
import { fakeOnboardingApi, ownerSession, renderPage } from './test-support';

async function fillShop(page: Awaited<ReturnType<typeof renderPage<SignupPage>>>, overrides: Partial<Record<'name' | 'owner' | 'email', string>> = {}) {
  await page.type('#nom-boutique', overrides.name ?? ' Ma Boutique ');
  await page.type('#nom-proprietaire', overrides.owner ?? 'Mamadou Konaté');
  await page.type('#adresse-email', overrides.email ?? ' Mamadou@Exemple.ci ');
  await page.check('input[type="checkbox"]', true);
}

async function toVerification(api = fakeOnboardingApi()) {
  const page = await renderPage(SignupPage, { api });
  await fillShop(page);
  await page.submit();
  return page;
}

describe('SignupPage', () => {
  it('starts on the shop step with plain words, a select of activities and no technical field', async () => {
    const page = await renderPage(SignupPage);

    expect(page.has('Étape 1 sur 2')).toBe(true);
    expect(page.has('Nom de votre boutique')).toBe(true);
    expect([...page.root.querySelectorAll('#activite option')].map((option) => option.textContent)).toContain('Quincaillerie');
    expect(page.root.querySelectorAll('input:not([type="checkbox"])').length).toBe(3);
    expect(page.has('conditions d’utilisation')).toBe(true);
  });

  it('explains every missing or wrong field next to it and sends nothing (SC-U2)', async () => {
    const page = await renderPage(SignupPage);

    await page.type('#adresse-email', 'pas-une-adresse');
    await page.submit();

    expect(page.api.startSignup).not.toHaveBeenCalled();
    expect(page.root.querySelector('#nom-boutique-erreur')?.textContent).toContain('nom de votre boutique');
    expect(page.root.querySelector('#nom-proprietaire-erreur')?.textContent).toContain('votre nom');
    expect(page.root.querySelector('#adresse-email-erreur')?.textContent).toContain('adresse e-mail n’est pas valide');
    expect(page.root.querySelector('#conditions-erreur')?.textContent).toContain('Cochez la case');
  });

  it('sends the shop with the terms version and moves on to the code, naming the address the code went to', async () => {
    const page = await toVerification();

    expect(page.api.startSignup).toHaveBeenCalledWith({
      email: 'Mamadou@Exemple.ci', commerceName: 'Ma Boutique', activity: 'Shop', ownerName: 'Mamadou Konaté', termsVersion: '2026-10-brouillon',
    });
    expect(page.has('Étape 2 sur 2')).toBe(true);
    expect(page.has('Mamadou@Exemple.ci')).toBe(true);
    expect(page.has('courriers indésirables')).toBe(true);
  });

  it('keeps the chosen activity', async () => {
    const page = await renderPage(SignupPage);
    await fillShop(page);
    await page.choose('#activite', 'Hardware');
    await page.submit();
    expect(page.api.startSignup).toHaveBeenCalledWith(expect.objectContaining({ activity: 'Hardware' }));
  });

  it('shows the server refusal in plain words and stays on the shop step', async () => {
    const api = fakeOnboardingApi();
    api.startSignup.mockRejectedValue(new ApiError(409, 'email_already_registered', 'Cette adresse e-mail a déjà un compte : connectez-vous avec votre mot de passe.'));
    const page = await toVerification(api);

    expect(page.has('Étape 1 sur 2')).toBe(true);
    expect(page.alertText()).toContain('a déjà un compte');
  });

  it('says what to do when the mail server is down (SC-U2)', async () => {
    const api = fakeOnboardingApi();
    api.startSignup.mockRejectedValue(new ApiError(503, 'email_not_sent', 'Nous n’avons pas pu envoyer l’e-mail. Vérifiez l’adresse ou réessayez dans quelques minutes.'));
    const page = await toVerification(api);

    expect(page.alertText()).toContain('Vérifiez l’adresse ou réessayez');
  });

  it('says the network is down without any technical word', async () => {
    const api = fakeOnboardingApi();
    api.startSignup.mockRejectedValue(new ApiError(0, 'network', ''));
    const page = await toVerification(api);

    expect(page.alertText()).toContain('Vérifiez votre connexion internet');
  });

  it('warns when offline', async () => {
    const page = await renderPage(SignupPage, { online: false });
    expect(page.has('hors ligne')).toBe(true);
  });

  it('checks the code and the password before asking the server, and writes the rule before the typing', async () => {
    const page = await toVerification();
    expect(page.has('Au moins 10 caractères, avec au moins une lettre et un chiffre.')).toBe(true);

    await page.type('#code', '12');
    await page.type('#mot-de-passe', 'court1');
    await page.submit();

    expect(page.api.completeSignup).not.toHaveBeenCalled();
    expect(page.root.querySelector('#code-erreur')?.textContent).toContain('6 chiffres');
    expect(page.root.querySelector('#mot-de-passe-erreur')?.textContent).toContain('10 caractères');
  });

  it('creates the shop, signs the owner in and goes to the dashboard without showing any identifier (SC-U1)', async () => {
    const page = await toVerification();

    await page.type('#code', '123 456');
    await page.type('#mot-de-passe', 'Boutique-2026-ok');
    await page.submit();

    expect(page.api.completeSignup).toHaveBeenCalledWith('v-1', '123456', 'Boutique-2026-ok');
    expect(page.auth.acceptOwnerSession).toHaveBeenCalledWith(ownerSession());
    expect(page.navigate).toHaveBeenCalledWith('/tableau-de-bord');
    expect(page.has('v-1')).toBe(false);
  });

  it('lets the owner see the password and hides it by default', async () => {
    const page = await toVerification();
    expect(page.field('#mot-de-passe').type).toBe('password');

    await page.check('input[type="checkbox"]', true);

    expect(page.field('#mot-de-passe').type).toBe('text');
  });

  it('brings a wrong code back to the code field with the remaining tries, and empties it', async () => {
    const api = fakeOnboardingApi();
    api.completeSignup.mockRejectedValue(new ApiError(422, 'invalid_code', 'Code incorrect. Il vous reste 4 essais.'));
    const page = await toVerification(api);
    await page.type('#code', '000000');
    await page.type('#mot-de-passe', 'Boutique-2026-ok');

    await page.submit();

    expect(page.root.querySelector('#code-erreur')?.textContent).toContain('Il vous reste 4 essais');
    expect(page.field('#code').value).toBe('');
    expect(page.navigate).not.toHaveBeenCalled();
  });

  it('tells how to start again when the request expired or the tries are used up', async () => {
    const api = fakeOnboardingApi();
    api.completeSignup.mockRejectedValue(new ApiError(422, 'verification_expired', 'Trop d’essais : recommencez l’inscription.'));
    const page = await toVerification(api);
    await page.type('#code', '000000');
    await page.type('#mot-de-passe', 'Boutique-2026-ok');

    await page.submit();

    expect(page.alertText()).toContain('recommencez l’inscription');
    expect(page.alertText()).toContain('Changer d’adresse e-mail');
  });

  it('puts a refused password under the password field (SC-U2)', async () => {
    const api = fakeOnboardingApi();
    api.completeSignup.mockRejectedValue(new ApiError(422, 'rule_violation', 'Le mot de passe ne doit pas contenir votre adresse e-mail.'));
    const page = await toVerification(api);
    await page.type('#code', '123456');
    await page.type('#mot-de-passe', 'mamadou-2026-xx');

    await page.submit();

    expect(page.root.querySelector('#mot-de-passe-erreur')?.textContent).toContain('adresse e-mail');
  });

  it('waits before allowing a new code, then resends and says the old code is dead', async () => {
    const api = fakeOnboardingApi();
    api.startSignup.mockResolvedValue({ verificationId: 'v-1', expiresAt: '2099-01-01', resendAvailableAt: new Date(Date.now() + 60_000).toISOString() });
    const page = await toVerification(api);
    const resend = () => [...page.root.querySelectorAll('button')].find((button) => button.textContent?.includes('Renvoyer le code'))!;

    expect(resend().disabled).toBe(true);
    expect(page.has('Possible dans')).toBe(true);

    api.startSignup.mockResolvedValue({ verificationId: 'v-1', expiresAt: '2099-01-01', resendAvailableAt: '2000-01-01T00:00:00.000Z' });
    const open = await toVerification();
    const openResend = [...open.root.querySelectorAll('button')].find((button) => button.textContent?.includes('Renvoyer le code'))!;
    expect(openResend.disabled).toBe(false);

    await open.click('Renvoyer le code');

    expect(open.api.resendCode).toHaveBeenCalledWith('v-1');
    expect(open.root.querySelector('[role="status"]')?.textContent).toContain('L’ancien ne marche plus');
  });

  it('goes back to the shop step to change the address', async () => {
    const page = await toVerification();

    await page.click('Changer d’adresse e-mail');

    expect(page.has('Étape 1 sur 2')).toBe(true);
    expect(page.field('#nom-boutique').value).toBe(' Ma Boutique ');
  });
});
