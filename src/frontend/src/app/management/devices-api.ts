import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { firstValueFrom, Observable } from 'rxjs';
import { API_ROOT, toApiError } from '../services/api.service';

export interface ManagedDevice {
  id: string;
  name: string;
  enrolledAt: string;
  lastSeenAt: string;
  isActive: boolean;
  revokedAt: string | null;
}

export interface DeviceInvitation {
  invitationId: string;
  token: string;
  expiresAt: string;
}

/** Un lien d'ajout d'appareil qui n'a pas encore servi. Ni le code ni le secret du lien ne sont rendus. */
export interface PendingLink {
  id: string;
  name: string;
  createdByName: string;
  createdAt: string;
  expiresAt: string;
}

/** Appareils du commerce : lecture, invitation et retrait, en ligne seulement. */
@Injectable({ providedIn: 'root' })
export class DevicesApi {
  private readonly http = inject(HttpClient);

  list(): Promise<ManagedDevice[]> {
    return this.call(this.http.get<ManagedDevice[]>(`${API_ROOT}/devices`));
  }

  invite(suggestedName: string | null): Promise<DeviceInvitation> {
    return this.call(this.http.post<DeviceInvitation>(`${API_ROOT}/devices/invitations`, { suggestedName }));
  }

  pendingInvitations(): Promise<PendingLink[]> {
    return this.call(this.http.get<PendingLink[]>(`${API_ROOT}/devices/invitations`));
  }

  async revokeInvitation(id: string): Promise<void> {
    await this.call(this.http.post(`${API_ROOT}/devices/invitations/${id}/revoke`, {}));
  }

  async revoke(id: string): Promise<void> {
    await this.call(this.http.post(`${API_ROOT}/devices/${id}/revoke`, {}));
  }

  private async call<T>(request: Observable<T>): Promise<T> {
    try {
      return await firstValueFrom(request);
    } catch (error) {
      throw toApiError(error);
    }
  }
}
