import 'fake-indexeddb/auto';
import { TestBed } from '@angular/core/testing';
import { Journal } from '../data/journal';
import { ApiError } from '../sync/sync-api';
import { ApiService } from './api.service';
import { AuthService, loginErrorMessage } from './auth.service';
import { JournalService } from './journal.service';

const TENANT = '3f2b8c1e-5d4a-4b7e-9c1d-2a6f8e0b1c3d';

async function setup(login = vi.fn()) {
  const journal = await Journal.open(`soro-auth-${Math.random()}`);
  TestBed.configureTestingModule({
    providers: [
      { provide: JournalService, useValue: { get: () => Promise.resolve(journal) } },
      { provide: ApiService, useValue: { login } },
    ],
  });
  return { auth: TestBed.inject(AuthService), journal, login };
}

describe('AuthService', () => {
  it('refuses a commerce code that is not a GUID', async () => {
    const { auth } = await setup();
    await expect(auth.configureDevice('pas-un-code')).rejects.toThrow(/code du commerce/);
    expect(auth.device()).toBeNull();
  });

  it('configures the device once with its own register and point of sale identifiers', async () => {
    const { auth, journal } = await setup();
    await auth.configureDevice(` ${TENANT} `);
    expect(auth.device()).toMatchObject({ tenantId: TENANT });
    expect(auth.device()?.registerId).toMatch(/^[0-9a-f-]{36}$/);
    expect(await journal.getDevice()).toEqual(auth.device());
  });

  it('logs in with the device commerce, stores the token and never keeps the PIN', async () => {
    const login = vi.fn().mockResolvedValue({
      accessToken: 'jwt', tokenType: 'Bearer', expiresAt: new Date(Date.now() + 3_600_000).toISOString(),
      user: { id: 'u', displayName: 'Awa', role: 'Cashier' },
    });
    const { auth, journal } = await setup(login);
    await auth.configureDevice(TENANT);

    await auth.login(' awa ', '1234');

    expect(login).toHaveBeenCalledWith(TENANT, 'awa', '1234');
    expect(auth.signedIn()).toBe(true);
    expect(auth.user()?.displayName).toBe('Awa');
    const stored = JSON.stringify(await journal.getToken());
    expect(stored).not.toContain('1234');
  });

  it('treats an expired token as signed out, and logout clears it', async () => {
    const { auth, journal } = await setup();
    await journal.saveToken({ value: 'old', expiresAt: new Date(Date.now() - 1000).toISOString(), user: { id: 'u', displayName: 'Awa', role: 'Cashier' } });
    await auth.load();
    expect(auth.signedIn()).toBe(false);

    await journal.saveToken({ value: 'fresh', expiresAt: new Date(Date.now() + 60_000).toISOString(), user: { id: 'u', displayName: 'Awa', role: 'Cashier' } });
    await auth.load();
    expect(auth.signedIn()).toBe(true);
    await auth.logout();
    expect(auth.signedIn()).toBe(false);
    expect(await journal.getToken()).toBeNull();
  });
});

describe('loginErrorMessage', () => {
  it('explains each failure in French', () => {
    expect(loginErrorMessage(new ApiError(401, 'invalid_credentials', ''))).toMatch(/incorrect/);
    expect(loginErrorMessage(new ApiError(423, 'account_locked', ''))).toMatch(/verrouillé/);
    expect(loginErrorMessage(new ApiError(0, 'network', ''))).toMatch(/injoignable/);
    expect(loginErrorMessage(new ApiError(429, 'x', ''))).toMatch(/Trop de tentatives/);
    expect(loginErrorMessage(new Error('?'))).toMatch(/échoué/);
  });
});
