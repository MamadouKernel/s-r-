import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { ApiError } from '../sync/sync-api';
import { AuthService } from '../services/auth.service';
import { CashService } from '../services/cash.service';
import { CatalogueService } from '../services/catalogue.service';
import { SyncService } from '../services/sync.service';
import { LoginPage } from './login-page';

async function render(login = vi.fn().mockResolvedValue(undefined), session: unknown = null) {
  TestBed.configureTestingModule({
    providers: [
      provideRouter([]),
      { provide: AuthService, useValue: { login } },
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
  return { root, type, submit, login, navigate };
}

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
