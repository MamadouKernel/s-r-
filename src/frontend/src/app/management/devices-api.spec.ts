import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { DevicesApi } from './devices-api';

describe('DevicesApi', () => {
  let api: DevicesApi;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    api = TestBed.inject(DevicesApi);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('lists the devices', async () => {
    const list = api.list();
    http.expectOne('/api/v1/devices').flush([{ id: 'd', name: 'Caisse 1', enrolledAt: 'x', lastSeenAt: 'y', isActive: true, revokedAt: null }]);
    expect((await list)[0].name).toBe('Caisse 1');
  });

  it('creates an invitation with the optional name', async () => {
    const invitation = api.invite('Caisse 2');
    const request = http.expectOne('/api/v1/devices/invitations');
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual({ suggestedName: 'Caisse 2' });
    request.flush({ invitationId: 'i', token: 'jeton', expiresAt: 'z' });
    expect((await invitation).token).toBe('jeton');

    const unnamed = api.invite(null);
    const second = http.expectOne('/api/v1/devices/invitations');
    expect(second.request.body).toEqual({ suggestedName: null });
    second.flush({ invitationId: 'i', token: 't', expiresAt: 'z' });
    await unnamed;
  });

  it('revokes a device on the right route', async () => {
    const revoked = api.revoke('d-1');
    const request = http.expectOne('/api/v1/devices/d-1/revoke');
    expect(request.request.method).toBe('POST');
    request.flush(null, { status: 204, statusText: 'No Content' });
    await revoked;
  });

  it('turns a refusal into an ApiError', async () => {
    const list = api.list();
    http.expectOne('/api/v1/devices').flush({ code: 'forbidden', detail: 'Seuls le propriétaire et le gérant peuvent voir les appareils.' }, { status: 403, statusText: 'Forbidden' });
    await expect(list).rejects.toMatchObject({ status: 403, code: 'forbidden' });
  });

  it('lists the pending links and cancels one on the right route', async () => {
    const pending = api.pendingInvitations();
    http.expectOne('/api/v1/devices/invitations').flush([{ id: 'i-1', name: 'Caisse 2', createdByName: 'Issa', createdAt: 'x', expiresAt: 'y' }]);
    expect((await pending)[0].createdByName).toBe('Issa');

    const cancelled = api.revokeInvitation('i-1');
    const request = http.expectOne('/api/v1/devices/invitations/i-1/revoke');
    expect(request.request.method).toBe('POST');
    request.flush(null, { status: 204, statusText: 'No Content' });
    await cancelled;
  });
});
