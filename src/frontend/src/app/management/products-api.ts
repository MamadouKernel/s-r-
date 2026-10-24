import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { firstValueFrom, Observable } from 'rxjs';
import { API_ROOT, toApiError } from '../services/api.service';

/** Produit tel que le serveur le renvoie : le prix d'achat est absent pour les profils qui n'ont pas le droit de le voir. */
export interface ManagedProduct {
  id: string;
  name: string;
  barcode: string | null;
  category: string | null;
  purchasePriceFcfa: number | null;
  salePriceFcfa: number;
  alertThreshold: number;
  tracksStock: boolean;
  isActive: boolean;
  quantityOnHand: number;
  isLowStock: boolean;
}

export type MovementKind = 'Reception' | 'Sale' | 'Loss' | 'SaleCancellation';

export interface StockMovement {
  id: string;
  kind: MovementKind;
  quantityDelta: number;
  reason: string | null;
  sourceId: string | null;
  userId: string;
  at: string;
  /** Prix d'achat unitaire d'une livraison (réception qui l'a indiqué), sinon `null`. */
  unitCostFcfa: number | null;
}

export interface ProductInput {
  name: string;
  barcode: string | null;
  category: string | null;
  purchasePriceFcfa: number;
  salePriceFcfa: number;
  alertThreshold: number;
}

export interface StockChange {
  movementId: string;
  quantityOnHand: number;
  alreadyProcessed: boolean;
  /** Prix d'achat moyen après une réception (RG-11), absent après une perte. */
  purchasePriceFcfa?: number | null;
}

/** Appels de gestion : directs au serveur, sans file locale (écrans en ligne seulement). Toute erreur devient une `ApiError`. */
@Injectable({ providedIn: 'root' })
export class ProductsApi {
  private readonly http = inject(HttpClient);

  list(options: { search?: string; includeInactive?: boolean } = {}): Promise<ManagedProduct[]> {
    let params = new HttpParams();
    if (options.search?.trim()) {
      params = params.set('search', options.search.trim());
    }
    if (options.includeInactive) {
      params = params.set('includeInactive', 'true');
    }
    return this.call(this.http.get<ManagedProduct[]>(`${API_ROOT}/products`, { params }));
  }

  lowStock(): Promise<ManagedProduct[]> {
    return this.call(this.http.get<ManagedProduct[]>(`${API_ROOT}/stock/alerts`));
  }

  async create(id: string, input: ProductInput & { tracksStock: boolean }): Promise<void> {
    await this.call(this.http.post(`${API_ROOT}/products`, { productId: id, ...input }));
  }

  async update(id: string, input: ProductInput): Promise<void> {
    await this.call(this.http.put(`${API_ROOT}/products/${id}`, input));
  }

  async deactivate(id: string): Promise<void> {
    await this.call(this.http.post(`${API_ROOT}/products/${id}/deactivate`, {}));
  }

  receive(
    productId: string,
    input: { movementId: string; quantity: number; supplier: string | null; purchasePriceFcfa: number | null },
  ): Promise<StockChange> {
    return this.call(this.http.post<StockChange>(`${API_ROOT}/products/${productId}/receptions`, input));
  }

  recordLoss(productId: string, input: { movementId: string; quantity: number; reason: string }): Promise<StockChange> {
    return this.call(this.http.post<StockChange>(`${API_ROOT}/products/${productId}/losses`, input));
  }

  movements(productId: string, limit = 50): Promise<StockMovement[]> {
    return this.call(
      this.http.get<StockMovement[]>(`${API_ROOT}/products/${productId}/movements`, {
        params: new HttpParams().set('limit', limit),
      }),
    );
  }

  private async call<T>(request: Observable<T>): Promise<T> {
    try {
      return await firstValueFrom(request);
    } catch (error) {
      throw toApiError(error);
    }
  }
}
