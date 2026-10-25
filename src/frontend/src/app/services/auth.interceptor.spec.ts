import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { FlashService } from '../management/flash.service';
import { authInterceptor } from './auth.interceptor';
import { AuthService } from './auth.service';

function setup(token: { value: string } | null = { value: 'jwt' }) {
  const auth = { token: () => token, markDeviceRefused: vi.fn().mockResolvedValue(undefined), logout: vi.fn().mockResolvedValue(undefined) };
  TestBed.configureTestingModule({
    providers: [provideRouter([]), provideHttpClient(withInterceptors([authInterceptor])), provideHttpClientTesting(), { provide: AuthService, useValue: auth }],
  });
  const navigate = vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);
  return { auth, navigate, http: TestBed.inject(HttpTestingController), client: TestBed.inject(HttpClient) };
}

describe('authInterceptor', () => {
  it('adds the token to the API calls but not to the login nor to other addresses', () => {
    const { client, http } = setup();

    client.get('/api/v1/products').subscribe();
    expect(http.expectOne('/api/v1/products').request.headers.get('Authorization')).toBe('Bearer jwt');

    client.post('/api/v1/auth/login', {}).subscribe();
    expect(http.expectOne('/api/v1/auth/login').request.headers.has('Authorization')).toBe(false);

    client.get('/ngsw.json').subscribe();
    expect(http.expectOne('/ngsw.json').request.headers.has('Authorization')).toBe(false);
  });

  it('forgets the device and goes to the welcome screen when the server no longer knows it (SC-U5)', async () => {
    const { auth, navigate, client, http } = setup();

    client.get('/api/v1/products').subscribe({ error: () => undefined });
    http.expectOne('/api/v1/products').flush({ code: 'device_not_authorized' }, { status: 401, statusText: 'Unauthorized' });
    await new Promise((resolve) => setTimeout(resolve, 0));

    expect(auth.markDeviceRefused).toHaveBeenCalledOnce();
    expect(navigate).toHaveBeenCalledWith('/bienvenue');
  });

  it('leaves the other failures alone and still reports them to the caller', async () => {
    const { auth, navigate, client, http } = setup();
    let status = 0;

    client.get('/api/v1/products').subscribe({ error: (error) => (status = error.status) });
    http.expectOne('/api/v1/products').flush({ code: 'forbidden' }, { status: 403, statusText: 'Forbidden' });
    await new Promise((resolve) => setTimeout(resolve, 0));

    expect(status).toBe(403);
    expect(auth.markDeviceRefused).not.toHaveBeenCalled();
    expect(navigate).not.toHaveBeenCalled();
  });

  it('signs out and goes to the login with a plain message when the access has changed, keeping the device', async () => {
    const { auth, navigate, client, http } = setup();

    client.get('/api/v1/products').subscribe({ error: () => undefined });
    http.expectOne('/api/v1/products').flush({ code: 'session_outdated' }, { status: 401, statusText: 'Unauthorized' });
    await new Promise((resolve) => setTimeout(resolve, 0));

    expect(auth.logout).toHaveBeenCalledOnce();
    expect(auth.markDeviceRefused).not.toHaveBeenCalled();
    expect(TestBed.inject(FlashService).current()).toBe('Votre accès a changé : reconnectez-vous.');
    expect(navigate).toHaveBeenCalledWith('/connexion');
  });
});
