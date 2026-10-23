import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { firstValueFrom, Observable } from 'rxjs';
import { API_ROOT, toApiError } from '../services/api.service';

export interface TeamMember {
  id: string;
  identifier: string;
  displayName: string;
  role: string;
  status: 'Active' | 'Disabled';
  createdAt: string;
  lastLoginAt: string | null;
  lockedUntil: string | null;
  canDisable: boolean;
  isSelf: boolean;
}

export interface Team {
  users: TeamMember[];
  assignableRoles: string[];
}

export interface DirectoryEntry {
  id: string;
  displayName: string;
}

export interface NewMember {
  identifier: string;
  displayName: string;
  role: string;
  pin: string;
}

/** Équipe : lecture et gestion, en ligne seulement. Le serveur calcule qui peut faire quoi ; l'écran n'en recopie pas la règle. */
@Injectable({ providedIn: 'root' })
export class TeamApi {
  private readonly http = inject(HttpClient);

  team(): Promise<Team> {
    return this.call(this.http.get<Team>(`${API_ROOT}/users`));
  }

  directory(): Promise<DirectoryEntry[]> {
    return this.call(this.http.get<DirectoryEntry[]>(`${API_ROOT}/users/directory`));
  }

  async create(id: string, member: NewMember): Promise<void> {
    await this.call(this.http.post(`${API_ROOT}/users`, { userId: id, ...member }));
  }

  async disable(id: string): Promise<void> {
    await this.call(this.http.post(`${API_ROOT}/users/${id}/disable`, {}));
  }

  private async call<T>(request: Observable<T>): Promise<T> {
    try {
      return await firstValueFrom(request);
    } catch (error) {
      throw toApiError(error);
    }
  }
}
