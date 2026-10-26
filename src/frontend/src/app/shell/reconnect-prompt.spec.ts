import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { ApiError } from '../sync/sync-api';
import { AuthService } from '../services/auth.service';
import { NetworkService } from '../services/network.service';
import { SyncService } from '../services/sync.service';
import { ReconnectPrompt } from './reconnect-prompt';

async function render(options: { online?: boolean; signedIn?: boolean; offline?: boolean; login?: ReturnType<typeof vi.fn> } = {}) {
  const online = signal(options.online ?? true);
  const signedIn = signal(options.signedIn ?? false);
  const offlineSession = signal(options.offline === false ? null : { userId: 'u-1', identifier: 'awa', displayName: 'Awa Koné', role: 'Cashier' });
  const login = options.login ?? vi.fn().mockResolvedValue(undefined);
  const requestFlush = vi.fn().mockResolvedValue(null);
  TestBed.configureTestingModule({
    providers: [
      { provide: NetworkService, useValue: { online } },
      { provide: AuthService, useValue: { signedIn, offlineSession, login } },
      { provide: SyncService, useValue: { requestFlush } },
    ],
  });
  const fixture = TestBed.createComponent(ReconnectPrompt);
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
  return { root, type, submit, login, requestFlush, online, signedIn, offlineSession, fixture };
}

describe('ReconnectPrompt (HLC-08)', () => {
  it('asks for the code when the network is back after an entry without the network', async () => {
    const { root } = await render();

    expect(root.textContent).toContain('Le réseau est revenu. Tapez votre code pour envoyer vos ventes.');
    expect(root.querySelector<HTMLInputElement>('#reconnexion-identifiant')!.value).toBe('awa');
  });

  it('prefills the identifier even when the entry without the network happens after the application opened', async () => {
    const { root, offlineSession, fixture } = await render({ offline: false });
    expect(root.querySelector('form')).toBeNull();

    offlineSession.set({ userId: 'u-2', identifier: 'kofi', displayName: 'Kofi', role: 'Manager' });
    await fixture.whenStable();

    expect(root.querySelector<HTMLInputElement>('#reconnexion-identifiant')!.value).toBe('kofi');
  });

  it.each([
    { online: false },
    { signedIn: true },
    { offline: false },
  ])('shows nothing otherwise (%o)', async (options) => {
    const { root } = await render(options);
    expect(root.querySelector('form')).toBeNull();
  });

  it('logs in online with the code, sends the queue and clears the field', async () => {
    const { root, type, submit, login, requestFlush } = await render();
    await type('#reconnexion-pin', '4821');

    await submit();

    expect(login).toHaveBeenCalledWith('awa', '4821');
    expect(requestFlush).toHaveBeenCalled();
    expect(root.querySelector<HTMLInputElement>('#reconnexion-pin')!.value).toBe('');
  });

  it('says why when the server refuses the code, without leaving the register', async () => {
    const refused = vi.fn().mockRejectedValue(new ApiError(401, 'invalid_credentials', ''));
    const { root, type, submit, requestFlush } = await render({ login: refused });
    await type('#reconnexion-pin', '0000');

    await submit();

    expect(root.querySelector('[role="alert"]')?.textContent).toContain('Identifiant ou code PIN incorrect.');
    expect(requestFlush).not.toHaveBeenCalled();
  });

  it('asks for a code before calling the server', async () => {
    const { root, submit, login } = await render();

    await submit();

    expect(login).not.toHaveBeenCalled();
    expect(root.querySelector('[role="alert"]')?.textContent).toContain('Tapez votre code');
  });
});
