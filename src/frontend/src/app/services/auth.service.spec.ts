import 'fake-indexeddb/auto';
import { TestBed } from '@angular/core/testing';
import { Journal } from '../data/journal';
import { OnboardingApi, OwnerSession } from '../onboarding/onboarding-api';
import { ApiError } from '../sync/sync-api';
import { ApiService } from './api.service';
import { OFFLINE_ITERATIONS } from '../core/offline-proof';
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
      // Peu de tours de calcul en test ; la valeur de production est contrôlée dans offline-proof.spec.ts.
      { provide: OFFLINE_ITERATIONS, useValue: 1000 },
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


const cashierLogin = (overrides = {}) => ({
  accessToken: 'jwt-cashier', tokenType: 'Bearer', expiresAt: new Date(Date.now() + 8 * 3_600_000).toISOString(),
  user: { id: 'u-1', displayName: 'Awa Koné', role: 'Cashier' }, ...overrides,
});

const OFFLINE_ERRORS = {
  wrong: 'Identifiant ou code incorrect.',
  expired: 'Il faut du réseau pour vous reconnecter.',
  blocked: 'Ce compte demande le réseau pour se reconnecter.',
};

async function enrolledCashier(login = vi.fn().mockResolvedValue(cashierLogin())) {
  const context = await setup({ login });
  await context.auth.enrollDevice('jeton', 'Caisse 1');
  await context.auth.login('Awa', '4821');
  await context.auth.logout();
  return context;
}

function reopen(journal: Journal) {
  TestBed.resetTestingModule();
  TestBed.configureTestingModule({
    providers: [
      { provide: JournalService, useValue: { get: () => Promise.resolve(journal) } },
      { provide: ApiService, useValue: { login: vi.fn() } },
      { provide: OnboardingApi, useValue: { enrollDevice: vi.fn() } },
      { provide: OFFLINE_ITERATIONS, useValue: 1000 },
    ],
  });
  return TestBed.inject(AuthService);
}

describe('AuthService: connexion sans réseau (HLC)', () => {
  afterEach(() => vi.useRealTimers());

  it('keeps a proof of the PIN after an online login of a cashier or a manager, never the PIN (HLC-01, HLC-04)', async () => {
    const { journal } = await enrolledCashier();

    const proofs = await journal.getProofs();
    expect(proofs).toHaveLength(1);
    expect(proofs[0]).toMatchObject({ userId: 'u-1', identifier: 'awa', role: 'Cashier', lockouts: 0, blocked: false });
    expect(JSON.stringify(await journal.getProofs())).not.toContain('4821');

  });

  it('keeps a proof for a manager too (HLC-04)', async () => {
    const manager = await setup({ login: vi.fn().mockResolvedValue(cashierLogin({ user: { id: 'm', displayName: 'Kofi', role: 'Manager' } })) });
    await manager.auth.enrollDevice('jeton', 'Caisse');

    await manager.auth.login('Kofi', '1234');

    expect((await manager.journal.getProofs())[0].role).toBe('Manager');
  });

  it('keeps no proof for a profile that cannot sell without the network', async () => {
    const { auth, journal } = await setup({ login: vi.fn().mockResolvedValue(cashierLogin({ user: { id: 's', displayName: 'Mariam', role: 'StockKeeper' } })) });
    await auth.enrollDevice('jeton', 'Caisse');

    await auth.login('Mariam', '1234');

    expect(await journal.getProofs()).toEqual([]);
  });

  it('replaces the proof of the same account at each online login', async () => {
    const { auth, journal } = await enrolledCashier();
    const before = (await journal.getProofs())[0];

    await auth.login('Awa', '4821');

    const after = await journal.getProofs();
    expect(after).toHaveLength(1);
    expect(after[0].salt).not.toBe(before.salt);
  });

  it('lets the cashier in without the network with the right code, with no token (HLC-03, HLC-07)', async () => {
    const { auth } = await enrolledCashier();

    await auth.loginOffline(' AWA ', '4821');

    expect(auth.offlineSession()).toMatchObject({ userId: 'u-1', displayName: 'Awa Koné', role: 'Cashier' });
    expect(auth.user()).toMatchObject({ role: 'Cashier' });
    expect(auth.token()).toBeNull();
    expect(auth.signedIn()).toBe(false);
  });

  it('answers the same for a wrong code and for an account this device does not know', async () => {
    const { auth } = await enrolledCashier();

    const wrong = await auth.loginOffline('awa', '0000').catch((error: Error) => error.message);
    const unknown = await auth.loginOffline('fantome', '4821').catch((error: Error) => error.message);

    expect(wrong).toBe(OFFLINE_ERRORS.wrong);
    expect(unknown).toBe(OFFLINE_ERRORS.wrong);
    expect(auth.offlineSession()).toBeNull();
  });

  it('refuses after three days without a connection, and forgets the proof (HLC-05)', async () => {
    vi.useFakeTimers({ toFake: ['Date'] });
    vi.setSystemTime(new Date('2026-10-26T08:00:00Z'));
    const { auth, journal } = await enrolledCashier();

    vi.setSystemTime(new Date('2026-10-29T08:00:01Z'));
    const message = await auth.loginOffline('awa', '4821').catch((error: Error) => error.message);

    expect(message).toBe(OFFLINE_ERRORS.expired);
    expect(await journal.getProofs()).toEqual([]);
  });

  it('locks for fifteen minutes after five wrong codes, even after a reload, and says how long (HLC-06)', async () => {
    vi.useFakeTimers({ toFake: ['Date'] });
    vi.setSystemTime(new Date('2026-10-26T08:00:00Z'));
    const { auth, journal } = await enrolledCashier();
    for (let attempt = 0; attempt < 5; attempt++) {
      await auth.loginOffline('awa', '0000').catch(() => undefined);
    }

    const reloaded = reopen(journal);
    await reloaded.load();
    const locked = await reloaded.loginOffline('awa', '4821').catch((error: Error) => error.message);
    vi.setSystemTime(new Date('2026-10-26T08:10:00Z'));
    const later = await reloaded.loginOffline('awa', '4821').catch((error: Error) => error.message);
    vi.setSystemTime(new Date('2026-10-26T08:15:01Z'));
    await reloaded.loginOffline('awa', '4821');

    expect(locked).toBe('Réessayez dans 15 minutes.');
    expect(later).toBe('Réessayez dans 5 minutes.');
    expect(reloaded.offlineSession()).not.toBeNull();
  });

  it('blocks the account offline after three locks in a row, until an online login (HLC-06)', async () => {
    vi.useFakeTimers({ toFake: ['Date'] });
    vi.setSystemTime(new Date('2026-10-26T08:00:00Z'));
    const { auth } = await enrolledCashier();
    for (let lock = 0; lock < 3; lock++) {
      vi.setSystemTime(new Date(Date.parse('2026-10-26T08:00:00Z') + lock * 3_600_000));
      for (let attempt = 0; attempt < 5; attempt++) {
        await auth.loginOffline('awa', '0000').catch(() => undefined);
      }
    }

    vi.setSystemTime(new Date('2026-10-26T12:00:00Z'));
    const blocked = await auth.loginOffline('awa', '4821').catch((error: Error) => error.message);
    await auth.login('Awa', '4821');
    await auth.logout();
    await auth.loginOffline('awa', '4821');

    expect(blocked).toBe(OFFLINE_ERRORS.blocked);
    expect(auth.offlineSession()).not.toBeNull();
  });

  it('keeps the offline session after a reload and ends it at logout, keeping the proof (HLC-07, HLC-09)', async () => {
    const { auth, journal } = await enrolledCashier();
    await auth.loginOffline('awa', '4821');

    const reloaded = reopen(journal);
    await reloaded.load();
    expect(reloaded.offlineSession()).toMatchObject({ userId: 'u-1' });

    await reloaded.logout();
    expect(reloaded.offlineSession()).toBeNull();
    expect(await journal.getProofs()).toHaveLength(1);
    expect(await reopen(journal).loginOffline('awa', '4821')).toBeUndefined();
  });

  it('forgets the proof of an account the server refuses (disabled or new PIN), but not on a network failure (HLC-09)', async () => {
    const login = vi.fn().mockResolvedValue(cashierLogin());
    const { auth, journal } = await enrolledCashier(login);

    login.mockRejectedValueOnce(new ApiError(0, 'network', ''));
    await auth.login('Awa', '4821').catch(() => undefined);
    expect(await journal.getProofs()).toHaveLength(1);

    login.mockRejectedValueOnce(new ApiError(401, 'invalid_credentials', ''));
    await auth.login('awa', '9999').catch(() => undefined);
    expect(await journal.getProofs()).toEqual([]);
  });

  it('forgets every proof and the offline session when the device is refused (HLC-09)', async () => {
    const { auth, journal } = await enrolledCashier();
    await auth.loginOffline('awa', '4821');

    await auth.markDeviceRefused();

    expect(await journal.getProofs()).toEqual([]);
    expect(auth.offlineSession()).toBeNull();
  });

  it('never calls the server for an offline login (HLC-12)', async () => {
    const { auth, login } = await enrolledCashier();
    login.mockClear();

    await auth.loginOffline('awa', '4821');
    await auth.loginOffline('awa', '0000').catch(() => undefined);

    expect(login).not.toHaveBeenCalled();
  });
});
