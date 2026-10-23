import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { CloseSessionOperation, OpenSessionOperation, SaleOperation } from '../data/journal';
import { CatalogueProduct } from '../domain/types';
import { ApiError, ClosingResult, SaleSyncStatus, SyncApi } from '../sync/sync-api';

export const API_ROOT = '/api/v1';

export interface LoginResponse {
  accessToken: string;
  tokenType: string;
  expiresAt: string;
  user: { id: string; displayName: string; role: string };
}

interface ProductDto {
  id: string;
  name: string;
  barcode: string | null;
  category: string | null;
  salePriceFcfa: number;
  tracksStock: boolean;
  isActive: boolean;
  quantityOnHand: number;
}

/**
 * Traduit une erreur HTTP en `ApiError` : `status` 0 = réseau injoignable ; le code stable et le message viennent du
 * `application/problem+json` du serveur (extension `code`, champ `detail`).
 */
export function toApiError(error: unknown): ApiError {
  if (error instanceof ApiError) {
    return error;
  }
  if (error instanceof HttpErrorResponse) {
    if (error.status === 0) {
      return new ApiError(0, 'network', 'Le serveur est injoignable.');
    }
    const body = (error.error ?? {}) as { code?: string; detail?: string; title?: string };
    return new ApiError(error.status, body.code ?? 'http_' + error.status, body.detail ?? body.title ?? 'Erreur du serveur.');
  }
  return new ApiError(0, 'network', 'Le serveur est injoignable.');
}

/** Client HTTP de la caisse. Réalise le contrat `SyncApi` attendu par le moteur de synchronisation. */
@Injectable({ providedIn: 'root' })
export class ApiService implements SyncApi {
  private readonly http = inject(HttpClient);

  async login(tenantId: string, identifier: string, pin: string): Promise<LoginResponse> {
    return this.call(this.http.post<LoginResponse>(`${API_ROOT}/auth/login`, { tenantId, identifier, pin }));
  }

  async products(): Promise<CatalogueProduct[]> {
    const dtos = await this.call(this.http.get<ProductDto[]>(`${API_ROOT}/products`));
    return dtos
      .filter((dto) => dto.isActive)
      .map((dto) => ({
        id: dto.id,
        name: dto.name,
        barcode: dto.barcode,
        category: dto.category,
        salePriceFcfa: dto.salePriceFcfa,
        tracksStock: dto.tracksStock,
        quantityOnHand: dto.quantityOnHand,
      }));
  }

  async openSession(operation: OpenSessionOperation): Promise<void> {
    await this.call(
      this.http.post(`${API_ROOT}/register-sessions`, {
        sessionId: operation.sessionId,
        pointOfSaleId: operation.pointOfSaleId,
        registerId: operation.registerId,
        openingFloatFcfa: operation.openingFloatFcfa,
        openedAt: operation.openedAt,
      }),
    );
  }

  async syncSales(sales: SaleOperation[]): Promise<SaleSyncStatus[]> {
    const response = await this.call(
      this.http.post<{ statuses: SaleSyncStatus[] }>(`${API_ROOT}/sales/sync`, {
        sales: sales.map((sale) => ({
          saleId: sale.saleId,
          sessionId: sale.sessionId,
          localSequence: sale.seq,
          at: sale.at,
          lines: sale.lines.map((line) => ({
            productId: line.productId,
            name: line.name,
            quantity: line.quantity,
            unitPriceFcfa: line.unitPriceFcfa,
          })),
          discountFcfa: sale.discountFcfa,
          payments: sale.payments.map((payment) => ({
            paymentId: payment.id,
            method: payment.method,
            amountFcfa: payment.amountFcfa,
            tenderedFcfa: payment.tenderedFcfa ?? null,
            reference: payment.reference?.trim() || null,
          })),
        })),
      }),
    );
    return response.statuses;
  }

  async closeSession(operation: CloseSessionOperation): Promise<ClosingResult> {
    return this.call(
      this.http.post<ClosingResult>(`${API_ROOT}/register-sessions/${operation.sessionId}/close`, {
        countedCashFcfa: operation.countedCashFcfa,
        at: operation.at,
      }),
    );
  }

  private async call<T>(request: import('rxjs').Observable<T>): Promise<T> {
    try {
      return await firstValueFrom(request);
    } catch (error) {
      throw toApiError(error);
    }
  }
}
