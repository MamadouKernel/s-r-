import { CloseSessionOperation, OpenSessionOperation, SaleOperation } from '../data/journal';

/** Échec d'appel au serveur. `status` vaut 0 quand le réseau est injoignable. */
export class ApiError extends Error {
  constructor(
    readonly status: number,
    readonly code: string,
    message: string,
  ) {
    super(message);
    this.name = 'ApiError';
  }
}

export interface SaleSyncStatus {
  saleId: string;
  outcome: 'Accepted' | 'AlreadyProcessed' | 'Rejected';
  errorCode?: string | null;
  message?: string | null;
}

export interface ClosingResult {
  expectedCashFcfa: number;
  countedCashFcfa: number;
  varianceFcfa: number;
}

/** Ce que le moteur de synchronisation attend du serveur (réalisé par le service HTTP). */
export interface SyncApi {
  openSession(operation: OpenSessionOperation): Promise<void>;
  syncSales(sales: SaleOperation[]): Promise<SaleSyncStatus[]>;
  closeSession(operation: CloseSessionOperation): Promise<ClosingResult>;
}
