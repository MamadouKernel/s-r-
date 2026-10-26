import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { ApiError } from '../sync/sync-api';
import { AuthService, OfflineLoginError } from '../services/auth.service';
import { CashService } from '../services/cash.service';
import { CatalogueService } from '../services/catalogue.service';
import { SyncService } from '../services/sync.service';
import { FlashService } from '../management/flash.service';
import { LoginPage } from './login-page';

async function render(
  login = vi.fn().mockResolvedValue(undefined),
  session: unknown = null,
  role: string | undefined = 'Cashier',
  loginOffline = vi.fn().mockResolvedValue(undefined),
) {
  TestBed.configureTestingModule({
    providers: [
      provideRouter([]),
      { provide: AuthService, useValue: { login, loginOffline, user: () => (role ? { id: 'u', displayName: 'X', role } : null) } },
      { provide: CashService, useValue: { session: signal(session).asReadonly() } },
      { provide: CatalogueService, useValue: { refresh: vi.fn().mockResolvedValue(undefined) } },
      { provide: SyncService, useValue: { requestFlush: vi.fn().mockResolvedValue(null) } },
    ],
  });
  const navigate = vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);
  const fixture = TestBed.createComponent(LoginPage);
  await fixture.whenStable();
  const root = fixture.nativeElement as HTMLElement;
  const type = async (selector: string, value: string) => {
    const input = root.querySelector<HTMLInputElement>(selector)!;
    input.value = value;
    input.dispatchEvent(new Event('input'));
    await fixture.whenStable();
  };
  const submit = async () => {
    root.querySelector('form')!.dispatchEvent(new Event('submit', { cancelable: true }));
    await fixture.whenStable();
  };
  const click = async (label: string) => {
    [...root.querySelectorAll('button')].find((button) => button.textContent?.includes(label))!.click();
    await fixture.whenStable();
  };
  return { root, type, submit, click, login, loginOffline, navigate };
}

describe('LoginPage: sans réseau (HLC-02, HLC-11)', () => {
  const noNetwork = () => vi.fn().mockRejectedValue(new ApiError(0, 'network', ''));

  it('offers to open the register without the network only when the server did not answer', async () => {
    const { root, type, submit, navigate } = await render(noNetwork());
    await type('#identifiant', 'awa');
    await type('#pin', '4821');

    await submit();

    expect(root.textContent).toContain('Pas de réseau. Vous pouvez ouvrir la caisse sans réseau avec votre code.');
    expect([...root.querySelectorAll('button')].some((button) => button.textContent?.includes('Ouvrir la caisse sans réseau'))).toBe(true);
    expect(navigate).not.toHaveBeenCalled();
  });

  it.each([502, 503, 504])('offers it too when the relay in front of the server answers %i, as the server itself did not answer', async (status) => {
    const { root, type, submit } = await render(vi.fn().mockRejectedValue(new ApiError(status, 'bad_gateway', '')));
    await type('#identifiant', 'awa');
    await type('#pin', '4821');

    await submit();

    expect(root.textContent).toContain('Ouvrir la caisse sans réseau');
  });

  it('never offers it when the server answered, even to refuse the code', async () => {
    const refused = vi.fn().mockRejectedValue(new ApiError(401, 'invalid_credentials', ''));
    const { root, type, submit, loginOffline } = await render(refused);
    await type('#identifiant', 'awa');
    await type('#pin', '0000');

    await submit();

    expect(root.textContent).not.toContain('sans réseau');
    expect(loginOffline).not.toHaveBeenCalled();
  });

  it('opens the register with the code typed, then goes where an online login would', async () => {
    const { type, submit, click, loginOffline, navigate } = await render(noNetwork());
    await type('#identifiant', ' Awa ');
    await type('#pin', '4821');
    await submit();

    await click('Ouvrir la caisse sans réseau');

    expect(loginOffline).toHaveBeenCalledWith(' Awa ', '4821');
    expect(navigate).toHaveBeenCalledWith('/ouverture');
  });

  it('says what is wrong and clears the code when the offline login is refused', async () => {
    const refused = vi.fn().mockRejectedValue(new OfflineLoginError('Réessayez dans 15 minutes.'));
    const { root, type, submit, click, navigate } = await render(noNetwork(), null, 'Cashier', refused);
    await type('#identifiant', 'awa');
    await type('#pin', '0000');
    await submit();

    await click('Ouvrir la caisse sans réseau');

    expect(root.querySelector('[role="alert"]')?.textContent).toContain('Réessayez dans 15 minutes.');
    expect(root.querySelector<HTMLInputElement>('#pin')!.value).toBe('');
    expect(navigate).not.toHaveBeenCalled();
  });
});

describe('LoginPage', () => {
  it('keeps the submit button active and explains what is missing instead', async () => {
    const { root, submit, login } = await render();
    expect(root.querySelector<HTMLButtonElement>('button[type="submit"]')!.disabled).toBe(false);

    await submit();

    expect(login).not.toHaveBeenCalled();
    expect(root.querySelector('[role="alert"]')?.textContent).toContain('Saisissez votre identifiant et votre code PIN');
  });

  it('logs in and goes to the opening screen when no session is open', async () => {
    const { type, submit, login, navigate } = await render();
    await type('#identifiant', ' awa ');
    await type('#pin', '4321');

    await submit();

    expect(login).toHaveBeenCalledWith(' awa ', '4321');
    expect(navigate).toHaveBeenCalledWith('/ouverture');
  });

  it('sends the owner to the dashboard after the login (SC-D8)', async () => {
    const { type, submit, navigate } = await render(undefined, null, 'Owner');
    await type('#identifiant', 'proprio');
    await type('#pin', '0000');
    await submit();
    expect(navigate).toHaveBeenCalledWith('/tableau-de-bord');
  });

  it('goes straight to the sale screen when a session is already open', async () => {
    const { type, submit, navigate } = await render(undefined, { sessionId: 's' });
    await type('#identifiant', 'awa');
    await type('#pin', '4321');
    await submit();
    expect(navigate).toHaveBeenCalledWith('/vente');
  });

  it('shows the French message of a refused login and clears the PIN', async () => {
    const { root, type, submit, navigate } = await render(vi.fn().mockRejectedValue(new ApiError(401, 'invalid_credentials', '')));
    await type('#identifiant', 'awa');
    await type('#pin', '0000');

    await submit();

    expect(root.querySelector('[role="alert"]')?.textContent).toContain('Identifiant ou code PIN incorrect');
    expect(root.querySelector<HTMLInputElement>('#pin')!.value).toBe('');
    expect(navigate).not.toHaveBeenCalled();
  });
});

describe('LoginPage and the device', () => {
  async function renderWith(login: ReturnType<typeof vi.fn>, device: object | null, flash = '') {
    const markDeviceRefused = vi.fn().mockResolvedValue(undefined);
    const flashService = new FlashService();
    if (flash) {
      flashService.show(flash);
    }
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        { provide: FlashService, useValue: flashService },
        { provide: AuthService, useValue: { login, markDeviceRefused, device: () => device, user: () => ({ id: 'u', displayName: 'X', role: 'Cashier' }) } },
        { provide: CashService, useValue: { session: signal(null).asReadonly() } },
        { provide: CatalogueService, useValue: { refresh: vi.fn().mockResolvedValue(undefined) } },
        { provide: SyncService, useValue: { requestFlush: vi.fn().mockResolvedValue(null) } },
      ],
    });
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);
    const fixture = TestBed.createComponent(LoginPage);
    await fixture.whenStable();
    const root = fixture.nativeElement as HTMLElement;
    const type = async (selector: string, value: string) => {
      const input = root.querySelector<HTMLInputElement>(selector)!;
      input.value = value;
      input.dispatchEvent(new Event('input'));
      await fixture.whenStable();
    };
    await type('#identifiant', 'awa');
    await type('#pin', '4321');
    root.querySelector('form')!.dispatchEvent(new Event('submit', { cancelable: true }));
    await fixture.whenStable();
    await new Promise((resolve) => setTimeout(resolve, 5));
    return { root, navigate, markDeviceRefused };
  }

  it('forgets a device the server removed and goes to the welcome screen, which explains it (SC-U5)', async () => {
    const { markDeviceRefused, navigate, root } = await renderWith(
      vi.fn().mockRejectedValue(new ApiError(403, 'device_not_authorized', 'x')), { tenantId: 't', deviceId: 'd', deviceSecret: 's' },
    );

    expect(markDeviceRefused).toHaveBeenCalledOnce();
    expect(navigate).toHaveBeenCalledWith('/bienvenue');
    expect(root.querySelector('[role="alert"]')?.textContent).toContain('nouveau lien');
  });

  it('only explains when the browser never had device credentials (owner browser), without forgetting anything', async () => {
    const { markDeviceRefused, navigate, root } = await renderWith(
      vi.fn().mockRejectedValue(new ApiError(403, 'device_not_authorized', 'x')), { tenantId: 't' },
    );

    expect(markDeviceRefused).not.toHaveBeenCalled();
    expect(navigate).not.toHaveBeenCalled();
    expect(root.querySelector('[role="alert"]')?.textContent).toContain('nouveau lien');
  });

  it('shows the message left by the device setup, and a way for the owner to sign in', async () => {
    const { root } = await renderWith(vi.fn().mockResolvedValue(undefined), { tenantId: 't' }, 'Cet appareil est prêt pour « Ma Boutique ».');

    expect(root.querySelector('[role="status"]')?.textContent).toContain('Cet appareil est prêt');
    expect(root.querySelector('a[href="/connexion-proprietaire"]')).toBeTruthy();
  });
});
