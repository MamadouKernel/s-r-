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
