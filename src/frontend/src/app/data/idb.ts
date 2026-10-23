// Utilitaires IndexedDB minimaux, sans dépendance. Règle d'or : dans une transaction on n'attend que des requêtes IndexedDB,
// sinon la transaction se valide toute seule avant la fin du travail.

export const OPERATIONS = 'operations';
export const PRODUCTS = 'products';
export const META = 'meta';
export const HELD = 'held';

export const DB_VERSION = 2;

/**
 * Ouvre la base en la migrant au besoin. Chaque version n'ajoute que ce qui lui est propre : les données d'une version
 * antérieure (journal, catalogue, appareil, jeton) ne sont jamais touchées par une migration.
 */
export function openDatabase(name: string): Promise<IDBDatabase> {
  return new Promise((resolve, reject) => {
    const request = indexedDB.open(name, DB_VERSION);
    request.onupgradeneeded = (event) => {
      const db = request.result;
      if (event.oldVersion < 1) {
        const operations = db.createObjectStore(OPERATIONS, { keyPath: 'seq' });
        // Un identifiant de vente ne figure qu'une fois dans le journal (double appui, rejeu).
        operations.createIndex('saleId', 'saleId', { unique: true });
        operations.createIndex('status', 'status');
        db.createObjectStore(PRODUCTS, { keyPath: 'id' });
        db.createObjectStore(META, { keyPath: 'key' });
      }
      if (event.oldVersion < 2) {
        // Ventes mises en attente (PWA-10).
        db.createObjectStore(HELD, { keyPath: 'id' });
      }
    };
    request.onsuccess = () => resolve(request.result);
    request.onerror = () => reject(request.error);
    request.onblocked = () => reject(new Error('La base locale est utilisée par un autre onglet : fermez-le puis réessayez.'));
  });
}

export function promisify<T>(request: IDBRequest<T>): Promise<T> {
  return new Promise((resolve, reject) => {
    request.onsuccess = () => resolve(request.result);
    request.onerror = () => reject(request.error);
  });
}

/**
 * Exécute `work` dans une transaction et ne rend la main qu'une fois celle-ci validée par IndexedDB : le résultat n'est donc
 * renvoyé qu'après écriture durable. Toute exception annule la transaction entière.
 */
export async function inTransaction<T>(
  db: IDBDatabase,
  stores: string[],
  mode: IDBTransactionMode,
  work: (tx: IDBTransaction) => Promise<T>,
): Promise<T> {
  const tx = db.transaction(stores, mode);
  const finished = new Promise<void>((resolve, reject) => {
    tx.oncomplete = () => resolve();
    tx.onerror = () => reject(tx.error);
    tx.onabort = () => reject(tx.error ?? new DOMException('Transaction annulée', 'AbortError'));
  });
  try {
    const result = await work(tx);
    await finished;
    return result;
  } catch (error) {
    finished.catch(() => undefined);
    try {
      tx.abort();
    } catch {
      // déjà terminée
    }
    throw error;
  }
}
