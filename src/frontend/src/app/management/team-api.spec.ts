import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ApiError } from '../sync/sync-api';
import { TeamApi } from './team-api';

describe('TeamApi', () => {
  let api: TeamApi;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    api = TestBed.inject(TeamApi);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('reads the team and the directory', async () => {
    const team = api.team();
    http.expectOne('/api/v1/users').flush({ users: [], assignableRoles: ['Cashier'] });
    expect((await team).assignableRoles).toEqual(['Cashier']);

    const directory = api.directory();
    http.expectOne('/api/v1/users/directory').flush([{ id: 'u', displayName: 'Awa' }]);
    expect(await directory).toEqual([{ id: 'u', displayName: 'Awa' }]);
  });

  it('creates a member with the client id and the contract field names', async () => {
    const created = api.create('u-1', { identifier: 'awa', displayName: 'Awa Koné', role: 'Cashier', pin: '4321' });
    const request = http.expectOne('/api/v1/users');
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual({ userId: 'u-1', identifier: 'awa', displayName: 'Awa Koné', role: 'Cashier', pin: '4321' });
    request.flush({ userId: 'u-1', alreadyProcessed: false });
    await created;
  });

  it('disables a member on the right route', async () => {
    const disabled = api.disable('u-1');
    const request = http.expectOne('/api/v1/users/u-1/disable');
    expect(request.request.method).toBe('POST');
    request.flush(null, { status: 204, statusText: 'No Content' });
    await disabled;
  });

  it('turns a taken identifier into an ApiError with its stable code', async () => {
    const created = api.create('u-1', { identifier: 'awa', displayName: 'A', role: 'Cashier', pin: '4321' });
    http.expectOne('/api/v1/users').flush(
      { title: 'Conflit', detail: 'Cet identifiant est déjà utilisé dans ce commerce.', code: 'identifier_taken' },
      { status: 409, statusText: 'Conflict' },
    );
    const error = await created.catch((caught: unknown) => caught);
    expect(error).toBeInstanceOf(ApiError);
    expect(error).toMatchObject({ status: 409, code: 'identifier_taken' });
  });

  it('gives a new pin, reactivates and changes a role on the right routes', async () => {
    const reset = api.resetPin('u-1', '4821');
    const first = http.expectOne('/api/v1/users/u-1/pin');
    expect(first.request.method).toBe('POST');
    expect(first.request.body).toEqual({ pin: '4821' });
    first.flush(null, { status: 204, statusText: 'No Content' });
    await reset;

    const enabled = api.enable('u-1', '6666');
    const second = http.expectOne('/api/v1/users/u-1/enable');
    expect(second.request.method).toBe('POST');
    expect(second.request.body).toEqual({ pin: '6666' });
    second.flush(null, { status: 204, statusText: 'No Content' });
    await enabled;

    const changed = api.changeRole('u-1', 'Manager');
    const third = http.expectOne('/api/v1/users/u-1/role');
    expect(third.request.method).toBe('PUT');
    expect(third.request.body).toEqual({ role: 'Manager' });
    third.flush(null, { status: 204, statusText: 'No Content' });
    await changed;
  });
});
