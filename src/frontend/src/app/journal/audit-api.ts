import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { firstValueFrom, Observable } from 'rxjs';
import { Range } from '../dashboard/period';
import { API_ROOT, toApiError } from '../services/api.service';

export interface AuditEntry {
  id: string;
  at: string;
  userId: string;
  action: string;
  resourceId: string;
  detail: string;
}

export interface AuditPage {
  entries: AuditEntry[];
  hasMore: boolean;
}

export interface AuditQuery {
  range: Range;
  category: string | null;
  limit: number;
  offset: number;
}

/** Journal des actions sensibles : lecture seule, directe au serveur, en ligne seulement. */
@Injectable({ providedIn: 'root' })
export class AuditApi {
  private readonly http = inject(HttpClient);

  list(query: AuditQuery): Promise<AuditPage> {
    let params = new HttpParams().set('from', query.range.from).set('to', query.range.to).set('limit', query.limit).set('offset', query.offset);
    if (query.category) {
      params = params.set('category', query.category);
    }
    return this.call(this.http.get<AuditPage>(`${API_ROOT}/audit-log`, { params }));
  }

  private async call<T>(request: Observable<T>): Promise<T> {
    try {
      return await firstValueFrom(request);
    } catch (error) {
      throw toApiError(error);
    }
  }
}
