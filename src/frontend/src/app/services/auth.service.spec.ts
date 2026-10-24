import 'fake-indexeddb/auto';
import { TestBed } from '@angular/core/testing';
import { Journal } from '../data/journal';
import { OnboardingApi, OwnerSession } from '../onboarding/onboarding-api';
import { ApiError } from '../sync/sync-api';
import { ApiService } from './api.service';
import { AuthService, loginErrorMessage } from './auth.service';
import { JournalService } from './journal.service';

const TENANT = '3f2b8c1e-5d4a-4b7e-9c1d-2a6f8e0b1c3d';
const OTHER_TENANT = '9a1b2c3d-0000-4000-8000-000000000001';

const ownerSession = (overrides: Partial<OwnerSession> = {}): OwnerSession => ({
  accessToken: 'jwt-owner', tokenType: 'Bearer', expiresAt: new Date(Date.now() + 3_600_000).toISOString(),
  user: { id: 'o', displayName: 'Mamadou', role: 'Owner' }, tenantId: TENANT, commerceName: 'Ma Boutique', trialEndsAt: null, ...overrides,
});

const enrolled = (overrides = {}) => ({
  deviceId: 'dev-1', deviceSecret: 'secret-1', tenantId: TENANT, commerceName: 'Ma Boutique', deviceName: 'Caisse 1', ...overrides,
});

async function setup(options: { login?: ReturnType<typeof vi.fn>; enrollDevice?: ReturnType<typeof vi.fn> } = {}) {
  const journal = await Journal.open(`soro-auth-${Math.random()}`);
  const login = options.login ?? vi.fn();
  const enrollDevice = options.enrollDevice ?? vi.fn().mockResolvedValue(enrolled());
  TestBed.configureTestingModule({
    providers: [
      { provide: JournalService, useValue: { get: () => Promise.resolve(journal) } },
      { provide: ApiService, useValue: { login } },
      { provide: OnboardingApi, useValue: { enrollDevice } },
    ],
  });
  return { auth: TestBed.inject(AuthService), journal, login, enrollDevice };
}

describe('AuthService', () => {
  it('learns the commerce from the owner session, with its own register and point of sale identifiers (no code to type)', async () => {
    const { auth, journal } = await setup();

    await auth.acceptOwnerSession(ownerSession());

    expect(auth.device()).toMatchObject({ tenantId: TENANT, commerceName: 'Ma Boutique' });
    expect(auth.device()?.registerId).toMatch(/^[0-9a-f-]{36}$/);
    expect(auth.device()?.deviceId).toBeUndefined();
    expect(await journal.getDevice()).toEqual(auth.device());
    expect(auth.signedIn()).toBe(true);
    expect(auth.user()).toMatchObject({ role: 'Owner' });
  });

  it('keeps the device identity when the same owner signs in again, and starts afresh for another commerce', async () => {
    const { auth } = await setup();
    await auth.enrollDevice('jeton', 'Caisse 1');
    const before = auth.device();

    await auth.acceptOwnerSession(ownerSession());
    expect(auth.device()).toMatchObject({ deviceId: 'dev-1', deviceSecret: 'secret-1', registerId: before?.registerId });

    await auth.acceptOwnerSession(ownerSession({ tenantId: OTHER_TENANT, commerceName: 'Autre' }));
    expect(auth.device()?.tenantId).toBe(OTHER_TENANT);
    expect(auth.device()?.deviceId).toBeUndefined();
    expect(auth.device()?.registerId).not.toBe(before?.registerId);
  });

  it('enrols from the link: keeps the device identity and the commerce name, trims the token and the name', async () => {
    const { auth, journal, enrollDevice } = await setup();

    const device = await auth.enrollDevice('  le-jeton  ', ' Caisse 1 ');

    expect(enrollDevice).toHaveBeenCalledWith('le-jeton', 'Caisse 1');
    expect(device).toMatchObject({ tenantId: TENANT, deviceId: 'dev-1', deviceSecret: 'secret-1', name: 'Caisse 1', commerceName: 'Ma Boutique' });
    expect(await journal.getDevice()).toEqual(device);
  });

  it('sends no name when the field is empty, so the server proposes its own', async () => {
    const { auth, enrollDevice } = await setup();
    await auth.enrollDevice('jeton', '   ');
    expect(enrollDevice).toHaveBeenCalledWith('jeton', null);
  });

  it('keeps the register of the device when it is enrolled again for the same commerce', async () => {
    const { auth } = await setup();
    await auth.acceptOwnerSession(ownerSession());
    const before = auth.device();

    await auth.enrollDevice('jeton', 'Caisse 1');

    expect(auth.device()?.registerId).toBe(before?.registerId);
    expect(auth.device()?.pointOfSaleId).toBe(before?.pointOfSaleId);
  });

  it('logs in with the device commerce and credentials, stores the token and never keeps the PIN', async () => {
    const login = vi.fn().mockResolvedValue({
      accessToken: 'jwt', tokenType: 'Bearer', expiresAt: new Date(Date.now() + 3_600_000).toISOString(),
      user: { id: 'u', displayName: 'Awa', role: 'Cashier' },
    });
    const { auth, journal } = await setup({ login });
    await auth.enrollDevice('jeton', 'Caisse 1');

    await auth.login(' awa ', '1234');

    expect(login).toHaveBeenCalledWith(TENANT, 'awa', '1234', { id: 'dev-1', secret: 'secret-1' });
    expect(auth.signedIn()).toBe(true);
    expect(auth.user()?.displayName).toBe('Awa');
    expect(JSON.stringify(await journal.getToken())).not.toContain('1234');
  });

  it('logs in without device credentials when none were given (owner browser)', async () => {
    const login = vi.fn().mockResolvedValue({
      accessToken: 'jwt', tokenType: 'Bearer', expiresAt: new Date(Date.now() + 3_600_000).toISOString(),
      user: { id: 'u', displayName: 'Awa', role: 'Cashier' },
    });
    const { auth } = await setup({ login });
    await auth.acceptOwnerSession(ownerSession());
    await auth.logout();

    await auth.login('awa', '1234');

    expect(login).toHaveBeenCalledWith(TENANT, 'awa', '1234', undefined);
  });

  it('forgets the credentials and the session when the server no longer knows the device, but keeps the commerce', async () => {
    const { auth, journal } = await setup();
    await auth.enrollDevice('jeton', 'Caisse 1');
    await auth.acceptOwnerSession(ownerSession());

    await auth.markDeviceRefused();

    expect(auth.device()).toMatchObject({ tenantId: TENANT, revoked: true });
    expect(auth.device()?.deviceId).toBeUndefined();
    expect(auth.device()?.deviceSecret).toBeUndefined();
    expect(auth.signedIn()).toBe(false);
    expect(await journal.getToken()).toBeNull();
    expect(await journal.getDevice()).toEqual(auth.device());
  });

  it('is a no-op to refuse a device that was never configured', async () => {
    const { auth } = await setup();
    await expect(auth.markDeviceRefused()).resolves.toBeUndefined();
    expect(auth.device()).toBeNull();
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

  it('tells a removed device what to do, in plain words and without any technical term', () => {
    const message = loginErrorMessage(new ApiError(403, 'device_not_authorized', 'x'));
    expect(message).toMatch(/nouveau lien/);
    expect(message).not.toMatch(/secret|token|jeton|403/i);
  });

  it('does not mistake another refusal for a removed device', () => {
    expect(loginErrorMessage(new ApiError(403, 'forbidden', ''))).toMatch(/échoué/);
  });
});
