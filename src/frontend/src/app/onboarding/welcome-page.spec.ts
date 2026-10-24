import { signal } from '@angular/core';
import { fakeAuth, renderPage } from './test-support';
import { WelcomePage } from './welcome-page';

describe('WelcomePage', () => {
  it('offers three clear choices in plain words and no technical field', async () => {
    const page = await renderPage(WelcomePage);

    expect(page.has('Créer ma boutique')).toBe(true);
    expect(page.has('J’ai déjà une boutique')).toBe(true);
    expect(page.has('J’ai reçu un lien')).toBe(true);
    expect(page.root.querySelector('input')).toBeNull(); // plus de « code du commerce » à taper
    expect(page.root.querySelector('a[href="/inscription"]')).toBeTruthy();
    expect(page.root.querySelector('a[href="/connexion-proprietaire"]')).toBeTruthy();
    expect(page.root.querySelector('[role="alert"]')).toBeNull();
  });

  it('says so when the device was removed, keeps the sales safe and tells what to do', async () => {
    const page = await renderPage(WelcomePage, { auth: fakeAuth({ device: signal({ tenantId: 't', revoked: true }) }) });

    expect(page.alertText()).toContain('n’est plus autorisé');
    expect(page.alertText()).toContain('Demandez un nouveau lien');
    expect(page.alertText()).toContain('ventes déjà enregistrées');
  });
});
