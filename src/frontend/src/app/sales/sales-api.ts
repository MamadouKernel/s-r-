import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { firstValueFrom, Observable } from 'rxjs';
import { MeasureUnit } from '../core/measure';
import { PaymentMethod } from '../domain/types';
import { API_ROOT, toApiError } from '../services/api.service';

export type SaleStatus = 'Validated' | 'Cancelled';

/** Une vente dans la liste « Ventes » : de quoi la reconnaître (n° de ticket, heure, total) et voir si elle a déjà un retour. */
export interface SaleSummary {
  id: string;
  ticket: number;
  recordedAt: string;
  cashierId: string;
  registerId: string;
  sessionId: string;
  netTotalFcfa: number;
  discountFcfa: number;
  status: SaleStatus;
  paymentMethods: PaymentMethod[];
  lineCount: number;
  returnedFcfa: number;
}

export interface SaleLineView {
  productId: string;
  productName: string;
  /** Pièces, ou grammes et millilitres pour un produit pesé ou mesuré (`unit`). */
  quantity: number;
  unitPriceFcfa: number;
  totalFcfa: number;
  unit?: MeasureUnit;
}

export interface SalePaymentView {
  method: PaymentMethod;
  amountFcfa: number;
  changeFcfa: number;
  reference: string | null;
}

export interface ReturnView {
  id: string;
  kind: 'Return' | 'Cancellation';
  at: string;
  by: string;
  approvedBy: string | null;
  reason: string;
  totalFcfa: number;
  cashFcfa: number;
  lines: { lineIndex: number; quantity: number; resellable: boolean }[];
  refund: { method: PaymentMethod; amountFcfa: number }[];
}

export interface SaleDetail {
  id: string;
  ticket: number;
  recordedAt: string;
  cashierId: string;
  registerId: string;
  sessionId: string;
  grossTotalFcfa: number;
  discountFcfa: number;
  netTotalFcfa: number;
  status: SaleStatus;
  lines: SaleLineView[];
  payments: SalePaymentView[];
  cancellation: { cancelledBy: string; reason: string; at: string } | null;
  returns: ReturnView[] | null;
}

/** Ce que le serveur a calculé pour un retour ou une annulation : de quoi dire « Rendez 3 000 FCFA » ou « Remboursez 1 500 FCFA par Wave ». */
export interface ReversalDone {
  returnId: string;
  totalFcfa: number;
  cashFcfa: number;
  creditFcfa: number;
  refund: { method: PaymentMethod; amountFcfa: number }[];
  alreadyProcessed: boolean;
}

export interface ApprovalGranted {
  token: string;
  approverId: string;
  approverName: string;
  expiresAt: string;
}

export interface GrantInput {
  identifier: string;
  pin: string;
  saleId: string;
  purpose: 'discount' | 'reversal' | 'cash-out';
  /** Pour une remise : le montant que le gérant voit et accepte. */
  amountFcfa: number;
}

export interface CancelInput {
  /** Fixé à l'ouverture de l'écran : un envoi répété ne rembourse jamais deux fois. */
  operationId: string;
  registerId: string;
  reason: string;
  resellable: boolean;
  approvalToken: string | null;
}

export interface ReturnInput {
  operationId: string;
  registerId: string;
  reason: string;
  lines: { lineIndex: number; quantity: number; resellable: boolean }[];
  approvalToken: string | null;
}

/** Ventes du commerce vues de la caisse : liste, détail, validation d'un gérant, annulation et retour. En ligne seulement. */
@Injectable({ providedIn: 'root' })
export class SalesApi {
  private readonly http = inject(HttpClient);

  list(limit = 100): Promise<SaleSummary[]> {
    return this.call(this.http.get<SaleSummary[]>(`${API_ROOT}/sales`, { params: new HttpParams().set('limit', limit) }));
  }

  detail(id: string): Promise<SaleDetail> {
    return this.call(this.http.get<SaleDetail>(`${API_ROOT}/sales/${id}`));
  }

  grant(input: GrantInput): Promise<ApprovalGranted> {
    return this.call(
      this.http.post<ApprovalGranted>(`${API_ROOT}/approvals`, {
        identifier: input.identifier,
        pin: input.pin,
        subjectId: input.saleId,
        purpose: input.purpose,
        amount: input.amountFcfa,
      }),
    );
  }

  cancel(id: string, input: CancelInput): Promise<ReversalDone> {
    return this.call(this.http.post<ReversalDone>(`${API_ROOT}/sales/${id}/cancel`, input));
  }

  returnLines(id: string, input: ReturnInput): Promise<ReversalDone> {
    return this.call(this.http.post<ReversalDone>(`${API_ROOT}/sales/${id}/returns`, input));
  }

  private async call<T>(request: Observable<T>): Promise<T> {
    try {
      return await firstValueFrom(request);
    } catch (error) {
      throw toApiError(error);
    }
  }
}
