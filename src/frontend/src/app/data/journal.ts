import { HELD, inTransaction, META, openDatabase, OPERATIONS, PRODUCTS, promisify } from './idb';
import { CartLine, CatalogueProduct, PaymentDraft } from '../domain/types';
import { cartTotal } from '../domain/cart';
import { referenceKey, validatePayments } from '../domain/payment';

export type OperationStatus = 'pending' | 'synced' | 'rejected';

interface OperationBase {
  /** Ordre de la caisse : attribué dans la transaction qui écrit l'opération, jamais réutilisé (RG-12). */
  seq: number;
  status: OperationStatus;
  createdAt: string;
  errorCode?: string;
  message?: string;
}

export interface OpenSessionOperation extends OperationBase {
  kind: 'openSession';
  sessionId: string;
  pointOfSaleId: string;
  registerId: string;
  openingFloatFcfa: number;
  openedAt: string;
}

export interface SaleOperation extends OperationBase {
  kind: 'sale';
  saleId: string;
  sessionId: string;
  at: string;
  lines: CartLine[];
  discountFcfa: number;
  payments: PaymentDraft[];
  netTotalFcfa: number;
  changeFcfa: number;
}

export interface CloseSessionOperation extends OperationBase {
  kind: 'closeSession';
  sessionId: string;
  countedCashFcfa: number;
  at: string;
  result?: { expectedCashFcfa: number; countedCashFcfa: number; varianceFcfa: number };
}

export type Operation = OpenSessionOperation | SaleOperation | CloseSessionOperation;

/**
 * L'appareil tel que l'application le retient : jamais montré à l'utilisateur. `deviceId` et `deviceSecret` viennent de l'enrôlement
 * (lien ou QR du propriétaire) ; ils manquent sur un navigateur où seul le propriétaire s'est connecté. `revoked` : le serveur a retiré cet
 * appareil, les ventes déjà enregistrées restent dans le journal.
 */
export interface Device {
  tenantId: string;
  registerId: string;
  pointOfSaleId: string;
  deviceId?: string;
  deviceSecret?: string;
  name?: string;
  commerceName?: string;
  revoked?: boolean;
}

export interface StoredToken {
  value: string;
  expiresAt: string;
  user: { id: string; displayName: string; role: string };
}

export interface LocalSession {
  sessionId: string;
  pointOfSaleId: string;
  registerId: string;
  openingFloatFcfa: number;
  openedAt: string;
}

export interface SaleInput {
  id: string;
  at: string;
  lines: CartLine[];
  payments: PaymentDraft[];
}

export interface SessionSummary {
  salesCount: number;
  netTotalFcfa: number;
  expectedCashFcfa: number;
}

/** Panier rangé pour servir le client suivant : ce n'est pas une vente (PWA-10). */
export interface HeldCart {
  id: string;
  label: string;
  lines: CartLine[];
  createdAt: string;
  /** Ordre de mise en attente, attribué par le journal : l'heure seule ne départage pas deux paniers rangés à la même milliseconde. */
  order?: number;
}

interface MetaEntry<T> {
  key: string;
  value: T;
}

/** Texte de recherche : minuscules, sans accents. */
function fold(text: string): string {
  return text.normalize('NFD').replace(/\p{Diacritic}/gu, '').toLowerCase();
}

/**
 * Journal local de la caisse (IndexedDB) : catalogue, opérations à envoyer et contexte d'appareil. Toute écriture qui touche
 * plusieurs éléments (vente + stock + séquence) se fait dans UNE transaction, avant tout accusé à l'écran (REQ-05).
 */
export class Journal {
  private constructor(private readonly db: IDBDatabase) {}

  static async open(name = 'soro-caisse'): Promise<Journal> {
    return new Journal(await openDatabase(name));
  }

  // ---- Contexte d'appareil et jeton ----------------------------------------------------------------

  getDevice(): Promise<Device | null> {
    return this.getMeta<Device>('device');
  }

  saveDevice(device: Device): Promise<void> {
    return this.putMeta('device', device);
  }

  getToken(): Promise<StoredToken | null> {
    return this.getMeta<StoredToken>('token');
  }

  saveToken(token: StoredToken): Promise<void> {
    return this.putMeta('token', token);
  }

  async clearToken(): Promise<void> {
    await inTransaction(this.db, [META], 'readwrite', async (tx) => {
      await promisify(tx.objectStore(META).delete('token'));
    });
  }

  // ---- Catalogue -------------------------------------------------------------------------------------

  /**
   * Remplace la copie du catalogue par celle du serveur. Les ventes encore en attente ne sont pas connues du serveur :
   * leurs quantités sont déduites à nouveau pour que le stock local reste juste (SC-P6).
   */
  async replaceCatalogue(products: CatalogueProduct[]): Promise<void> {
    await inTransaction(this.db, [PRODUCTS, OPERATIONS], 'readwrite', async (tx) => {
      const store = tx.objectStore(PRODUCTS);
      await promisify(store.clear());
      const pending = (await promisify(tx.objectStore(OPERATIONS).getAll())) as Operation[];
      const sold = new Map<string, number>();
      for (const operation of pending) {
        if (operation.kind === 'sale' && operation.status === 'pending') {
          for (const line of operation.lines) {
            sold.set(line.productId, (sold.get(line.productId) ?? 0) + line.quantity);
          }
        }
      }
      for (const product of products) {
        const adjusted = product.tracksStock
          ? { ...product, quantityOnHand: product.quantityOnHand - (sold.get(product.id) ?? 0) }
          : product;
        await promisify(store.put(adjusted));
      }
    });
  }

  async listProducts(term?: string): Promise<CatalogueProduct[]> {
    const all = (await this.readAll<CatalogueProduct>(PRODUCTS)).sort((a, b) => a.name.localeCompare(b.name, 'fr'));
    const needle = term ? fold(term.trim()) : '';
    return needle === '' ? all : all.filter((product) => fold(product.name).includes(needle) || product.barcode === term?.trim());
  }

  async findByBarcode(barcode: string): Promise<CatalogueProduct | null> {
    const code = barcode.trim();
    return (await this.readAll<CatalogueProduct>(PRODUCTS)).find((product) => product.barcode === code) ?? null;
  }

  // ---- Session de caisse -----------------------------------------------------------------------------

  currentSession(): Promise<LocalSession | null> {
    return this.getMeta<LocalSession>('session');
  }

  async openSession(input: LocalSession): Promise<OpenSessionOperation> {
    return inTransaction(this.db, [OPERATIONS, META], 'readwrite', async (tx) => {
      if (await this.metaIn<LocalSession>(tx, 'session')) {
        throw new Error('Une session de caisse est déjà ouverte.');
      }
      if (!Number.isInteger(input.openingFloatFcfa) || input.openingFloatFcfa < 0) {
        throw new Error('Le fond de caisse doit être un montant entier, positif ou nul.');
      }
      const operation: OpenSessionOperation = {
        kind: 'openSession',
        ...input,
        seq: await this.nextSequence(tx),
        status: 'pending',
        createdAt: new Date().toISOString(),
      };
      await promisify(tx.objectStore(OPERATIONS).put(operation));
      await promisify(tx.objectStore(META).put({ key: 'session', value: input } satisfies MetaEntry<LocalSession>));
      return operation;
    });
  }

  /** Enregistre la vente, baisse le stock local et réserve son numéro de séquence dans une seule transaction. */
  async recordSale(input: SaleInput): Promise<SaleOperation> {
    if (input.lines.length === 0) {
      throw new Error('Le panier est vide.');
    }
    const netTotal = cartTotal(input.lines);

    return inTransaction(this.db, [OPERATIONS, PRODUCTS, META], 'readwrite', async (tx) => {
      const operations = tx.objectStore(OPERATIONS);

      // Rejeu ou double appui : la vente existe déjà, on la renvoie telle quelle.
      const existing = (await promisify(operations.index('saleId').get(input.id))) as SaleOperation | undefined;
      if (existing) {
        return existing;
      }

      const session = await this.metaIn<LocalSession>(tx, 'session');
      if (!session) {
        throw new Error("Aucune session de caisse n'est ouverte.");
      }

      const used = this.referencesOf((await promisify(operations.getAll())) as Operation[]);
      const errors = validatePayments(netTotal, input.payments, used);
      if (errors.length > 0) {
        throw new Error(errors.join(' '));
      }

      const change = input.payments
        .filter((payment) => payment.method === 'Cash')
        .reduce((sum, payment) => sum + Math.max((payment.tenderedFcfa ?? payment.amountFcfa) - payment.amountFcfa, 0), 0);

      const sale: SaleOperation = {
        kind: 'sale',
        seq: await this.nextSequence(tx),
        status: 'pending',
        createdAt: new Date().toISOString(),
        saleId: input.id,
        sessionId: session.sessionId,
        at: input.at,
        lines: input.lines,
        discountFcfa: 0,
        payments: input.payments,
        netTotalFcfa: netTotal,
        changeFcfa: change,
      };
      await promisify(operations.put(sale));
      await this.decrementStock(tx, input.lines);
      return sale;
    });
  }

  async sessionSummary(): Promise<SessionSummary | null> {
    const session = await this.currentSession();
    if (!session) {
      return null;
    }
    const sales = (await this.operations()).filter(
      (operation): operation is SaleOperation =>
        operation.kind === 'sale' && operation.sessionId === session.sessionId && operation.status !== 'rejected',
    );
    const cash = sales
      .flatMap((sale) => sale.payments)
      .filter((payment) => payment.method === 'Cash')
      .reduce((sum, payment) => sum + payment.amountFcfa, 0);
    return {
      salesCount: sales.length,
      netTotalFcfa: sales.reduce((sum, sale) => sum + sale.netTotalFcfa, 0),
      expectedCashFcfa: session.openingFloatFcfa + cash,
    };
  }

  async closeSession(input: { countedCashFcfa: number; at: string }): Promise<CloseSessionOperation> {
    return inTransaction(this.db, [OPERATIONS, META, HELD], 'readwrite', async (tx) => {
      const session = await this.metaIn<LocalSession>(tx, 'session');
      if (!session) {
        throw new Error("Aucune session de caisse n'est ouverte.");
      }
      if (!Number.isInteger(input.countedCashFcfa) || input.countedCashFcfa < 0) {
        throw new Error('Le montant compté doit être un montant entier, positif ou nul.');
      }
      const operation: CloseSessionOperation = {
        kind: 'closeSession',
        seq: await this.nextSequence(tx),
        status: 'pending',
        createdAt: new Date().toISOString(),
        sessionId: session.sessionId,
        countedCashFcfa: input.countedCashFcfa,
        at: input.at,
      };
      await promisify(tx.objectStore(OPERATIONS).put(operation));
      await promisify(tx.objectStore(META).delete('session'));
      // Les ventes en attente et le panier en cours ne sont pas des ventes : ils ne survivent pas à la session.
      await promisify(tx.objectStore(HELD).clear());
      await promisify(tx.objectStore(META).delete('draftCart'));
      return operation;
    });
  }

  // ---- Panier durable et ventes en attente (PWA-10) --------------------------------------------------

  async loadDraft(): Promise<CartLine[]> {
    return (await this.getMeta<CartLine[]>('draftCart')) ?? [];
  }

  /** Enregistre le panier en cours à chaque modification ; un panier vide efface le brouillon. */
  async saveDraft(lines: readonly CartLine[]): Promise<void> {
    await inTransaction(this.db, [META], 'readwrite', async (tx) => {
      const store = tx.objectStore(META);
      if (lines.length === 0) {
        await promisify(store.delete('draftCart'));
      } else {
        await promisify(store.put({ key: 'draftCart', value: [...lines] } satisfies MetaEntry<CartLine[]>));
      }
    });
  }

  /** Range le panier : aucune opération n'est créée, le stock et la séquence ne bougent pas. */
  async holdCart(input: HeldCart): Promise<HeldCart> {
    if (input.lines.length === 0) {
      throw new Error('Le panier est vide : il n’y a rien à mettre en attente.');
    }
    return inTransaction(this.db, [HELD, META], 'readwrite', async (tx) => {
      // Compteur propre aux ventes en attente : la séquence d'envoi des opérations n'est jamais consommée.
      const order = ((await this.metaIn<number>(tx, 'heldOrder')) ?? 0) + 1;
      await promisify(tx.objectStore(META).put({ key: 'heldOrder', value: order } satisfies MetaEntry<number>));
      const held: HeldCart = { ...input, order };
      await promisify(tx.objectStore(HELD).put(held));
      await promisify(tx.objectStore(META).delete('draftCart'));
      return held;
    });
  }

  /** Reprend une vente en attente dans le panier courant, qui doit être vide : aucune fusion implicite. */
  async resumeHeld(id: string): Promise<CartLine[]> {
    return inTransaction(this.db, [HELD, META], 'readwrite', async (tx) => {
      const draft = await this.metaIn<CartLine[]>(tx, 'draftCart');
      if (draft && draft.length > 0) {
        throw new Error('Le panier en cours n’est pas vide : mettez-le en attente ou videz-le avant de reprendre.');
      }
      const held = (await promisify(tx.objectStore(HELD).get(id))) as HeldCart | undefined;
      if (!held) {
        throw new Error('Vente en attente introuvable.');
      }
      await promisify(tx.objectStore(HELD).delete(id));
      await promisify(tx.objectStore(META).put({ key: 'draftCart', value: held.lines } satisfies MetaEntry<CartLine[]>));
      return held.lines;
    });
  }

  async deleteHeld(id: string): Promise<void> {
    await inTransaction(this.db, [HELD], 'readwrite', async (tx) => {
      await promisify(tx.objectStore(HELD).delete(id));
    });
  }

  async listHeld(): Promise<HeldCart[]> {
    return (await this.readAll<HeldCart>(HELD)).sort((a, b) => (a.order ?? 0) - (b.order ?? 0));
  }

  async heldCount(): Promise<number> {
    return (await this.readAll<HeldCart>(HELD)).length;
  }

  // ---- File d'opérations et synchronisation ----------------------------------------------------------

  async operations(): Promise<Operation[]> {
    return (await this.readAll<Operation>(OPERATIONS)).sort((a, b) => a.seq - b.seq);
  }

  async pendingOperations(): Promise<Operation[]> {
    return (await this.operations()).filter((operation) => operation.status === 'pending');
  }

  async counts(): Promise<Record<OperationStatus, number>> {
    const counts: Record<OperationStatus, number> = { pending: 0, synced: 0, rejected: 0 };
    for (const operation of await this.operations()) {
      counts[operation.status]++;
    }
    return counts;
  }

  /** Clés des références Mobile Money déjà utilisées sur l'appareil (les ventes rejetées ne comptent plus). */
  async usedReferences(): Promise<Set<string>> {
    return this.referencesOf(await this.operations());
  }

  markSynced(seq: number, patch: Partial<CloseSessionOperation> = {}): Promise<void> {
    return this.update(seq, { ...patch, status: 'synced', errorCode: undefined, message: undefined });
  }

  markRejected(seq: number, errorCode: string, message: string): Promise<void> {
    return this.update(seq, { status: 'rejected', errorCode, message });
  }

  // ---- Interne ---------------------------------------------------------------------------------------

  private referencesOf(operations: Operation[]): Set<string> {
    const keys = new Set<string>();
    for (const operation of operations) {
      if (operation.kind !== 'sale' || operation.status === 'rejected') {
        continue;
      }
      for (const payment of operation.payments) {
        if (payment.method !== 'Cash' && payment.method !== 'Credit' && payment.reference?.trim()) {
          keys.add(referenceKey(payment.method, payment.reference));
        }
      }
    }
    return keys;
  }

  private async update(seq: number, patch: Partial<Operation>): Promise<void> {
    await inTransaction(this.db, [OPERATIONS], 'readwrite', async (tx) => {
      const store = tx.objectStore(OPERATIONS);
      const current = (await promisify(store.get(seq))) as Operation | undefined;
      if (!current) {
        throw new Error(`Opération ${seq} introuvable.`);
      }
      await promisify(store.put({ ...current, ...patch }));
    });
  }

  private async decrementStock(tx: IDBTransaction, lines: CartLine[]): Promise<void> {
    const store = tx.objectStore(PRODUCTS);
    const quantities = new Map<string, number>();
    for (const line of lines) {
      quantities.set(line.productId, (quantities.get(line.productId) ?? 0) + line.quantity);
    }
    for (const [productId, quantity] of quantities) {
      const product = (await promisify(store.get(productId))) as CatalogueProduct | undefined;
      if (product?.tracksStock) {
        await promisify(store.put({ ...product, quantityOnHand: product.quantityOnHand - quantity }));
      }
    }
  }

  private async nextSequence(tx: IDBTransaction): Promise<number> {
    const current = (await this.metaIn<number>(tx, 'sequence')) ?? 0;
    const next = current + 1;
    await promisify(tx.objectStore(META).put({ key: 'sequence', value: next } satisfies MetaEntry<number>));
    return next;
  }

  private async metaIn<T>(tx: IDBTransaction, key: string): Promise<T | null> {
    const entry = (await promisify(tx.objectStore(META).get(key))) as MetaEntry<T> | undefined;
    return entry?.value ?? null;
  }

  private getMeta<T>(key: string): Promise<T | null> {
    return inTransaction(this.db, [META], 'readonly', (tx) => this.metaIn<T>(tx, key));
  }

  private putMeta<T>(key: string, value: T): Promise<void> {
    return inTransaction(this.db, [META], 'readwrite', async (tx) => {
      await promisify(tx.objectStore(META).put({ key, value } satisfies MetaEntry<T>));
    });
  }

  private readAll<T>(store: string): Promise<T[]> {
    return inTransaction(this.db, [store], 'readonly', (tx) => promisify(tx.objectStore(store).getAll()) as Promise<T[]>);
  }
}
