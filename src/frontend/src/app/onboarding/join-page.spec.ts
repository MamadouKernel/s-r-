import { ApiError } from '../sync/sync-api';
import { JoinPage } from './join-page';
import { fakeAuth, renderPage } from './test-support';

describe('JoinPage', () => {
  it('asks only for the optional name of the device, empty so that the name planned by the manager is kept; the token is never shown (SC-U3)', async () => {
    const page = await renderPage(JoinPage, { fragment: 'jeton-secret-tres-long' });

    expect(page.has('Rejoindre la boutique')).toBe(true);
    expect(page.field('#nom-appareil').value).toBe('');
    expect(page.field('#nom-appareil').placeholder).toBe('Caisse');
    expect(page.root.querySelectorAll('input').length).toBe(1);
    expect(page.has('jeton-secret-tres-long')).toBe(false);
  });

  it('explains an incomplete link and what to do, without any form (SC-U4)', async () => {
    const page = await renderPage(JoinPage, { fragment: null });

    expect(page.alertText()).toContain('Ce lien est incomplet');
    expect(page.alertText()).toContain('demandez un nouveau lien');
    expect(page.root.querySelector('form')).toBeNull();
  });

  it('configures the device, leaves a message for the login and goes there', async () => {
    const auth = fakeAuth({ enrollDevice: vi.fn().mockResolvedValue({ commerceName: 'Quincaillerie Koffi' }) });
    const page = await renderPage(JoinPage, { fragment: 'le-jeton', auth });
    await page.type('#nom-appareil', 'Caisse du fond');

    await page.submit();

    expect(auth.enrollDevice).toHaveBeenCalledWith('le-jeton', 'Caisse du fond');
    expect(page.flash.current()).toContain('Quincaillerie Koffi');
    expect(page.flash.current()).toContain('code PIN');
    expect(page.navigate).toHaveBeenCalledWith('/connexion');
  });

  it('explains a used or expired link in the words of the server and stays on the page (SC-U4)', async () => {
    const auth = fakeAuth({
      enrollDevice: vi.fn().mockRejectedValue(new ApiError(422, 'invalid_invitation', 'Ce lien a déjà été utilisé. Demandez-en un nouveau au propriétaire.')),
    });
    const page = await renderPage(JoinPage, { fragment: 'le-jeton', auth });

    await page.submit();

    expect(page.alertText()).toContain('déjà été utilisé');
    expect(page.alertText()).toContain('Demandez-en un nouveau');
    expect(page.navigate).not.toHaveBeenCalled();
  });

  it('sends no name when the field is left empty, so the manager’s name is kept', async () => {
    const auth = fakeAuth({ enrollDevice: vi.fn().mockResolvedValue({ commerceName: 'B' }) });
    const page = await renderPage(JoinPage, { fragment: 'le-jeton', auth });

    await page.submit();

    expect(auth.enrollDevice).toHaveBeenCalledWith('le-jeton', '');
  });

  it('says the network is down in plain words', async () => {
    const auth = fakeAuth({ enrollDevice: vi.fn().mockRejectedValue(new ApiError(0, 'network', '')) });
    const page = await renderPage(JoinPage, { fragment: 'le-jeton', auth });

    await page.submit();

    expect(page.alertText()).toContain('Vérifiez votre connexion internet');
  });
});
