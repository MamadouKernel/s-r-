import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { firstValueFrom, Observable } from 'rxjs';
import { MeasureUnit } from '../core/measure';
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
  /** Famille et format (docs/specs/formats-produits) ; absents d'une réponse d'avant les formats. */
  familyId?: string;
  familyName?: string;
  formatLabel?: string | null;
  photoId?: string | null;
  /** Photo de la famille seule : la différence avec `photoId` dit si le format a la sienne. */
  familyPhotoId?: string | null;
  /** Unité de vente ; absente d'une réponse d'avant la vente au poids (= pièce). Les quantités d'un produit pesé sont en grammes ou millilitres. */
  unit?: MeasureUnit;
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
  /** Format du produit dans sa famille ; `name` est alors le nom de la famille. */
  formatLabel?: string | null;
  /** Renvoyée telle quelle à la modification : le serveur refuse de la changer dès que le produit a un stock ou un historique. */
  unit?: MeasureUnit;
}

/** Un format à créer : son identifiant est fixé à l'ouverture du formulaire pour qu'un nouvel essai ne le crée pas deux fois. */
export interface FormatInput {
  productId: string;
  label: string | null;
  barcode: string | null;
  purchasePriceFcfa: number;
  salePriceFcfa: number;
  alertThreshold: number;
  tracksStock: boolean;
  unit?: MeasureUnit;
}

export interface CreateFamilyInput {
  familyId: string;
  name: string;
  category: string | null;
  formats: FormatInput[];
}

export type PhotoOwner = 'family' | 'format';

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

  /** Le serveur fabrique le code interne (EAN-13, préfixe 29) d'un produit sans code-barres et le lui donne. */
  async createInternalBarcode(id: string): Promise<string> {
    const answer = await this.call(this.http.post<{ barcode: string }>(`${API_ROOT}/products/${id}/internal-barcode`, {}));
    return answer.barcode;
  }

  async update(id: string, input: ProductInput): Promise<void> {
    await this.call(this.http.put(`${API_ROOT}/products/${id}`, input));
  }

  async createFamily(input: CreateFamilyInput): Promise<void> {
    await this.call(this.http.post(`${API_ROOT}/product-families`, input));
  }

  /** `labelForExistingFormat` : le libellé à donner au format actuel quand la famille n'en a qu'un, sans libellé. */
  async addFormat(familyId: string, format: FormatInput, labelForExistingFormat: string | null = null): Promise<void> {
    await this.call(this.http.post(`${API_ROOT}/product-families/${familyId}/formats`, { format, labelForExistingFormat }));
  }

  async updateFamily(familyId: string, input: { name: string; category: string | null }): Promise<void> {
    await this.call(this.http.put(`${API_ROOT}/product-families/${familyId}`, input));
  }

  async group(input: { familyId: string; name: string; category: string | null; members: { productId: string; label: string }[] }): Promise<void> {
    await this.call(this.http.post(`${API_ROOT}/product-families/group`, input));
  }

  /** Envoie l'image (déjà réduite) et renvoie l'identifiant de la nouvelle photo. */
  async setPhoto(owner: PhotoOwner, ownerId: string, image: Blob): Promise<string> {
    const route = owner === 'family' ? 'product-families' : 'products';
    const response = await this.call(
      this.http.put<{ photoId: string }>(`${API_ROOT}/${route}/${ownerId}/photo`, image, { headers: { 'Content-Type': image.type || 'image/jpeg' } }),
    );
    return response.photoId;
  }

  async removePhoto(owner: PhotoOwner, ownerId: string): Promise<void> {
    const route = owner === 'family' ? 'product-families' : 'products';
    await this.call(this.http.delete(`${API_ROOT}/${route}/${ownerId}/photo`));
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
