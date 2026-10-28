import { CashMovementOperation, CloseSessionOperation, OpenSessionOperation, SaleOperation } from '../data/journal';

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

/**
 * Le résultat d'un comptage. L'attendu et l'écart sont vides pour un caissier (clôture à l'aveugle) ; `needsRecount` : la session reste ouverte et
 * le serveur veut un second comptage.
 */
export interface ClosingResult {
  closed: boolean;
  needsRecount: boolean;
  countedCashFcfa: number;
  expectedCashFcfa: number | null;
  varianceFcfa: number | null;
}

/** Ce que le moteur de synchronisation attend du serveur (réalisé par le service HTTP). */
export interface SyncApi {
  openSession(operation: OpenSessionOperation): Promise<void>;
  syncSales(sales: SaleOperation[]): Promise<SaleSyncStatus[]>;
  recordMovement(operation: CashMovementOperation): Promise<void>;
  closeSession(operation: CloseSessionOperation): Promise<ClosingResult>;
}
