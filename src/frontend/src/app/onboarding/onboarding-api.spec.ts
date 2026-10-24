import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ApiError } from '../sync/sync-api';
import { OnboardingApi } from './onboarding-api';

describe('OnboardingApi', () => {
  let api: OnboardingApi;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    api = TestBed.inject(OnboardingApi);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  const shop = { email: 'a@b.ci', commerceName: 'Ma Boutique', activity: 'Shop', ownerName: 'Mamadou', termsVersion: 'v1' };

  it('starts a signup with the contract field names', async () => {
    const started = api.startSignup(shop);
    const request = http.expectOne('/api/v1/signup/start');
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual(shop);
    request.flush({ verificationId: 'v', expiresAt: 'x', resendAvailableAt: 'y' });
    expect((await started).verificationId).toBe('v');
  });

  it('resends and completes with the verification id the app kept', async () => {
    const resent = api.resendCode('v');
    const resend = http.expectOne('/api/v1/signup/resend');
    expect(resend.request.body).toEqual({ verificationId: 'v' });
    resend.flush({ verificationId: 'v', expiresAt: 'x', resendAvailableAt: 'y' });
    await resent;

    const done = api.completeSignup('v', '123456', 'Mot-de-passe-1');
    const request = http.expectOne('/api/v1/signup/complete');
    expect(request.request.body).toEqual({ verificationId: 'v', code: '123456', password: 'Mot-de-passe-1' });
    request.flush({ accessToken: 'jwt' });
    expect((await done).accessToken).toBe('jwt');
  });

  it('logs the owner in by e-mail and password', async () => {
    const session = api.ownerLogin('a@b.ci', 'Mot-de-passe-1');
    const request = http.expectOne('/api/v1/auth/owner/login');
    expect(request.request.body).toEqual({ email: 'a@b.ci', password: 'Mot-de-passe-1' });
    request.flush({ accessToken: 'jwt', tenantId: 't' });
    expect((await session).tenantId).toBe('t');
  });

  it('starts and confirms a password reset', async () => {
    const started = api.startPasswordReset('a@b.ci');
    const start = http.expectOne('/api/v1/owner/password-reset/start');
    expect(start.request.body).toEqual({ email: 'a@b.ci' });
    start.flush(null, { status: 202, statusText: 'Accepted' });
    await started;

    const confirmed = api.confirmPasswordReset('a@b.ci', '123456', 'Nouveau-mot-2');
    const request = http.expectOne('/api/v1/owner/password-reset/confirm');
    expect(request.request.body).toEqual({ email: 'a@b.ci', code: '123456', newPassword: 'Nouveau-mot-2' });
    request.flush(null, { status: 204, statusText: 'No Content' });
    await confirmed;
  });

  it('enrols a device with the token and the optional name', async () => {
    const enrolled = api.enrollDevice('le-jeton', null);
    const request = http.expectOne('/api/v1/devices/enroll');
    expect(request.request.body).toEqual({ token: 'le-jeton', deviceName: null });
    request.flush({ deviceId: 'd', deviceSecret: 's', tenantId: 't', commerceName: 'B', deviceName: 'Caisse' });
    expect((await enrolled).deviceSecret).toBe('s');
  });

  it('turns the problem answer of the server into an ApiError with its code and French message', async () => {
    const failed = api.completeSignup('v', '000000', 'x');
    http.expectOne('/api/v1/signup/complete').flush(
      { code: 'invalid_code', detail: 'Code incorrect. Il vous reste 4 essais.' },
      { status: 422, statusText: 'Unprocessable' },
    );
    await expect(failed).rejects.toMatchObject({ status: 422, code: 'invalid_code', message: 'Code incorrect. Il vous reste 4 essais.' });
    await failed.catch((error) => expect(error).toBeInstanceOf(ApiError));
  });
});
