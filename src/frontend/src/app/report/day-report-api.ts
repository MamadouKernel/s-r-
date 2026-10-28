import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { MeasureUnit } from '../core/measure';
import { PaymentMethod } from '../domain/types';
import { API_ROOT, toApiError } from '../services/api.service';

export interface DayCashier {
  cashierId: string;
  salesCount: number;
  netTotalFcfa: number;
  discountsCount: number;
  discountFcfa: number;
  cancellationsCount: number;
  cancelledFcfa: number;
  returnsCount: number;
  returnedFcfa: number;
  /** Somme des écarts de ses caisses clôturées : négatif = manque, positif = excédent. */
  varianceFcfa: number;
  varianceSessions: number;
}

export interface DaySession {
  id: string;
  cashierId: string;
  openedAt: string;
  closedAt: string | null;
  status: 'Open' | 'Closed';
  openingFloatFcfa: number;
  expectedCashFcfa: number;
  countedCashFcfa: number | null;
  varianceFcfa: number | null;
  needsClosing: boolean;
  drawerAboveWarning: boolean;
}

export interface DayReport {
  day: string;
  salesCount: number;
  netTotalFcfa: number;
  averageBasketFcfa: number;
  payments: { method: PaymentMethod; amountFcfa: number; count: number }[];
  cashiers: DayCashier[];
  movements: { kind: 'Expense' | 'Drop' | 'Topup'; reason: string | null; count: number; amountFcfa: number }[];
  sessions: DaySession[];
  debtCreatedFcfa: number;
  repaymentsFcfa: number;
  lowStock: { name: string; quantity: number; threshold: number; unit: MeasureUnit }[];
  names: Record<string, string>;
}

/** Le rapport d'une journée (docs/specs/cloture-sorties-rapport CLO-07), lu directement au serveur : en ligne seulement. */
@Injectable({ providedIn: 'root' })
export class DayReportApi {
  private readonly http = inject(HttpClient);

  async day(date: string): Promise<DayReport> {
    try {
      return await firstValueFrom(this.http.get<DayReport>(`${API_ROOT}/reports/day`, { params: new HttpParams().set('date', date) }));
    } catch (error) {
      throw toApiError(error);
    }
  }
}
