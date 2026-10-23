import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { firstValueFrom, Observable } from 'rxjs';
import { API_ROOT, toApiError } from '../services/api.service';
import { PaymentLine } from './metrics';
import { Range } from './period';

export interface ActivityReport {
  from: string;
  to: string;
  salesCount: number;
  netTotalFcfa: number;
  discountTotalFcfa: number;
  cancelledCount: number;
  cancelledTotalFcfa: number;
  byPaymentMethod: PaymentLine[];
  byCashier: { cashierId: string; salesCount: number; netTotalFcfa: number }[];
  sessionsClosed: number;
  shortfallFcfa: number;
  surplusFcfa: number;
}

export interface SessionSummary {
  id: string;
  cashierId: string;
  registerId: string;
  openedAt: string;
  openingFloatFcfa: number;
  cashReceivedFcfa: number;
  expectedCashFcfa: number;
  status: 'Open' | 'Closed';
  countedCashFcfa: number | null;
  varianceFcfa: number | null;
  closedAt: string | null;
}

/** Lectures du tableau de bord : directes au serveur, sans file locale (écran en ligne seulement). */
@Injectable({ providedIn: 'root' })
export class DashboardApi {
  private readonly http = inject(HttpClient);

  activity(range: Range): Promise<ActivityReport> {
    const params = new HttpParams().set('from', range.from).set('to', range.to);
    return this.call(this.http.get<ActivityReport>(`${API_ROOT}/reports/activity`, { params }));
  }

  /** Caisses actuellement ouvertes, quelle que soit leur date d'ouverture (une caisse oubliée date de la veille). */
  openSessions(): Promise<SessionSummary[]> {
    const params = new HttpParams().set('status', 'Open').set('limit', 50);
    return this.call(this.http.get<SessionSummary[]>(`${API_ROOT}/register-sessions`, { params }));
  }

  /**
   * Sessions clôturées avec un écart. Le serveur filtre sur l'heure d'ouverture : on lit donc à partir de `openedFrom`
   * (la veille), et l'appelant garde celles clôturées aujourd'hui, car une caisse ouverte hier soir se clôture ce matin.
   */
  closedWithVariance(openedFrom: string, openedTo: string): Promise<SessionSummary[]> {
    const params = new HttpParams()
      .set('status', 'Closed')
      .set('onlyWithVariance', 'true')
      .set('from', openedFrom)
      .set('to', openedTo)
      .set('limit', 100);
    return this.call(this.http.get<SessionSummary[]>(`${API_ROOT}/register-sessions`, { params }));
  }

  private async call<T>(request: Observable<T>): Promise<T> {
    try {
      return await firstValueFrom(request);
    } catch (error) {
      throw toApiError(error);
    }
  }
}
