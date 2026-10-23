// Utilitaires IndexedDB minimaux, sans dépendance. Règle d'or : dans une transaction on n'attend que des requêtes IndexedDB,
// sinon la transaction se valide toute seule avant la fin du travail.

export const OPERATIONS = 'operations';
export const PRODUCTS = 'products';
export const META = 'meta';

export function openDatabase(name: string): Promise<IDBDatabase> {
  return new Promise((resolve, reject) => {
    const request = indexedDB.open(name, 1);
    request.onupgradeneeded = () => {
      const db = request.result;
      const operations = db.createObjectStore(OPERATIONS, { keyPath: 'seq' });
      // Un identifiant de vente ne figure qu'une fois dans le journal (double appui, rejeu).
      operations.createIndex('saleId', 'saleId', { unique: true });
      operations.createIndex('status', 'status');
      db.createObjectStore(PRODUCTS, { keyPath: 'id' });
      db.createObjectStore(META, { keyPath: 'key' });
    };
    request.onsuccess = () => resolve(request.result);
    request.onerror = () => reject(request.error);
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
