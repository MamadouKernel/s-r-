import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { API_ROOT, toApiError } from '../services/api.service';

export type SubscriptionStatus = 'Trial' | 'Active' | 'Grace' | 'ReadOnly';
export type SubscriptionPeriod = 'Monthly' | 'Annual';
export type SubscriptionMethod = 'Wave' | 'OrangeMoney' | 'MtnMomo' | 'MoovMoney';
export type PaymentStatus = 'Pending' | 'Validated' | 'Refused';

export interface SubscriptionPayment {
  id: string;
  period: SubscriptionPeriod;
  method: SubscriptionMethod;
  reference: string;
  amountFcfa: number;
  status: PaymentStatus;
  declaredAt: string;
  decidedAt: string | null;
  reason: string | null;
  coveredTo: string | null;
}

/** L'état d'abonnement tel que le serveur le donne ; prix, numéros et paiements sont absents pour ceux qui ne gèrent pas la boutique. */
export interface SubscriptionView {
  status: SubscriptionStatus;
  endsAt: string;
  readOnlyFrom: string;
  daysLeft: number;
  isReadOnly: boolean;
  monthlyPriceFcfa: number | null;
  annualPriceFcfa: number | null;
  payNumbers: Partial<Record<SubscriptionMethod, string>> | null;
  payments: SubscriptionPayment[] | null;
}

export interface DeclareInput {
  paymentId: string;
  period: SubscriptionPeriod;
  method: SubscriptionMethod;
  reference: string;
  amountFcfa: number;
}

/** Appels d'abonnement (docs/specs/abonnement). Toute erreur devient une `ApiError`. */
@Injectable({ providedIn: 'root' })
export class SubscriptionApi {
  private readonly http = inject(HttpClient);

  async get(): Promise<SubscriptionView> {
    try {
      return await firstValueFrom(this.http.get<SubscriptionView>(`${API_ROOT}/subscription`));
    } catch (error) {
      throw toApiError(error);
    }
  }

  async declare(input: DeclareInput): Promise<void> {
    try {
      await firstValueFrom(this.http.post(`${API_ROOT}/subscription/payments`, input));
    } catch (error) {
      throw toApiError(error);
    }
  }
}
