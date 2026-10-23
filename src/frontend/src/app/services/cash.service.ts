import { computed, inject, Injectable, signal } from '@angular/core';
import { CloseSessionOperation, LocalSession, SaleOperation, SessionSummary } from '../data/journal';
import { addProduct, cartTotal, removeLine, setQuantity } from '../domain/cart';
import { CartLine, CatalogueProduct, PaymentDraft } from '../domain/types';
import { AuthService } from './auth.service';
import { CatalogueService } from './catalogue.service';
import { JournalService } from './journal.service';
import { SyncService } from './sync.service';

/** Parcours du caissier : session, panier, encaissement et clôture. Tout passe par le journal local avant tout accusé (REQ-05). */
@Injectable({ providedIn: 'root' })
export class CashService {
  private readonly journalService = inject(JournalService);
  private readonly auth = inject(AuthService);
  private readonly catalogue = inject(CatalogueService);
  private readonly sync = inject(SyncService);

  private readonly sessionSignal = signal<LocalSession | null>(null);
  private readonly cartSignal = signal<CartLine[]>([]);
  private readonly lastSaleSignal = signal<SaleOperation | null>(null);
  private readonly submittingSignal = signal(false);
  private checkoutId: string | null = null;
  private inFlight: Promise<SaleOperation> | null = null;

  readonly session = this.sessionSignal.asReadonly();
  readonly cart = this.cartSignal.asReadonly();
  readonly total = computed(() => cartTotal(this.cartSignal()));
  readonly lastSale = this.lastSaleSignal.asReadonly();
  readonly submitting = this.submittingSignal.asReadonly();

  async load(): Promise<void> {
    this.sessionSignal.set(await (await this.journalService.get()).currentSession());
  }

  async openSession(openingFloatFcfa: number): Promise<void> {
    const device = this.auth.device();
    if (!device) {
      throw new Error('Cet appareil n’est pas configuré.');
    }
    const session: LocalSession = {
      sessionId: crypto.randomUUID(),
      pointOfSaleId: device.pointOfSaleId,
      registerId: device.registerId,
      openingFloatFcfa,
      openedAt: new Date().toISOString(),
    };
    await (await this.journalService.get()).openSession(session);
    this.sessionSignal.set(session);
    void this.sync.requestFlush();
  }

  // ---- Panier ------------------------------------------------------------------------------------

  add(product: CatalogueProduct, quantity = 1): void {
    this.cartSignal.update((cart) => addProduct(cart, product, quantity));
  }

  /** Scan : ajoute le produit au code-barres exact ; renvoie `false` si le code est inconnu. */
  async addByBarcode(code: string): Promise<boolean> {
    const product = await this.catalogue.findByBarcode(code);
    if (!product) {
      return false;
    }
    this.add(product);
    return true;
  }

  changeQuantity(productId: string, quantity: number): void {
    this.cartSignal.update((cart) => setQuantity(cart, productId, quantity));
  }

  remove(productId: string): void {
    this.cartSignal.update((cart) => removeLine(cart, productId));
  }

  clearCart(): void {
    this.cartSignal.set([]);
    this.checkoutId = null;
  }

  // ---- Encaissement ------------------------------------------------------------------------------

  /** Références Mobile Money déjà utilisées sur l'appareil, pour les contrôler pendant la saisie (SC-P5). */
  async usedReferences(): Promise<Set<string>> {
    return (await this.journalService.get()).usedReferences();
  }

  /**
   * Enregistre la vente. L'identifiant est créé une fois par encaissement et conservé en cas de nouvel essai : un double appui, ou
   * un nouvel appui après une erreur, ne peut donc jamais produire deux ventes (SC-P4).
   */
  confirmSale(payments: PaymentDraft[]): Promise<SaleOperation> {
    if (this.inFlight) {
      return this.inFlight;
    }
    this.checkoutId ??= crypto.randomUUID();
    const input = { id: this.checkoutId, at: new Date().toISOString(), lines: this.cartSignal(), payments };
    this.submittingSignal.set(true);
    this.inFlight = (async () => {
      try {
        const sale = await (await this.journalService.get()).recordSale(input);
        this.lastSaleSignal.set(sale);
        this.cartSignal.set([]);
        this.checkoutId = null;
        await this.catalogue.loadLocal();
        void this.sync.requestFlush();
        return sale;
      } finally {
        this.submittingSignal.set(false);
        this.inFlight = null;
      }
    })();
    return this.inFlight;
  }

  // ---- Clôture -----------------------------------------------------------------------------------

  async summary(): Promise<SessionSummary | null> {
    return (await this.journalService.get()).sessionSummary();
  }

  async closeSession(countedCashFcfa: number): Promise<CloseSessionOperation> {
    const closing = await (await this.journalService.get()).closeSession({
      countedCashFcfa,
      at: new Date().toISOString(),
    });
    this.sessionSignal.set(null);
    this.cartSignal.set([]);
    void this.sync.requestFlush();
    return closing;
  }
}
