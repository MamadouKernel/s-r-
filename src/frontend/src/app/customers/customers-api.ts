import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { firstValueFrom, Observable } from 'rxjs';
import { CustomerSnapshot, PaymentMethod } from '../domain/types';
import { API_ROOT, toApiError } from '../services/api.service';

export interface Repayment {
  id: string;
  amountFcfa: number;
  method: PaymentMethod;
  reference: string | null;
  userId: string;
  at: string;
}

export interface NewRepayment {
  repaymentId: string;
  amountFcfa: number;
  method: PaymentMethod;
  reference: string | null;
}

/** Clients, dettes et versements : appels directs au serveur, en ligne seulement. Toute erreur devient une `ApiError`. */
@Injectable({ providedIn: 'root' })
export class CustomersApi {
  private readonly http = inject(HttpClient);

  list(search: string | null, includeInactive = false): Promise<CustomerSnapshot[]> {
    let params = new HttpParams().set('includeInactive', includeInactive);
    if (search) {
      params = params.set('search', search);
    }
    return this.call(this.http.get<CustomerSnapshot[]>(`${API_ROOT}/customers`, { params }));
  }

  create(input: { customerId: string; name: string; phone: string | null; creditCeilingFcfa: number | null }): Promise<{ customerId: string; alreadyProcessed: boolean; creditCeilingFcfa: number }> {
    return this.call(this.http.post<{ customerId: string; alreadyProcessed: boolean; creditCeilingFcfa: number }>(`${API_ROOT}/customers`, input));
  }

  update(id: string, input: { name: string; phone: string | null; creditCeilingFcfa: number }): Promise<CustomerSnapshot> {
    return this.call(this.http.put<CustomerSnapshot>(`${API_ROOT}/customers/${id}`, input));
  }

  deactivate(id: string): Promise<CustomerSnapshot> {
    return this.call(this.http.post<CustomerSnapshot>(`${API_ROOT}/customers/${id}/deactivate`, {}));
  }

  repay(customerId: string, input: NewRepayment): Promise<{ repaymentId: string; debtFcfa: number; alreadyProcessed: boolean }> {
    return this.call(this.http.post<{ repaymentId: string; debtFcfa: number; alreadyProcessed: boolean }>(`${API_ROOT}/customers/${customerId}/repayments`, input));
  }

  repayments(customerId: string, limit = 50): Promise<Repayment[]> {
    return this.call(this.http.get<Repayment[]>(`${API_ROOT}/customers/${customerId}/repayments`, { params: new HttpParams().set('limit', limit) }));
  }

  private async call<T>(request: Observable<T>): Promise<T> {
    try {
      return await firstValueFrom(request);
    } catch (error) {
      throw toApiError(error);
    }
  }
}
