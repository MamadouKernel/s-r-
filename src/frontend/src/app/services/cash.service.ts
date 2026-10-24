import { computed, inject, Injectable, signal } from '@angular/core';
import { CloseSessionOperation, HeldCart, LocalSession, SaleOperation, SessionSummary } from '../data/journal';
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
  private readonly heldSignal = signal<HeldCart[]>([]);
  private readonly lastSaleSignal = signal<SaleOperation | null>(null);
  private readonly submittingSignal = signal(false);
  private checkoutId: string | null = null;
  private inFlight: Promise<SaleOperation> | null = null;
  /** Écritures du brouillon, en file : elles arrivent dans l'ordre et jamais après un « mettre en attente ». */
  private draftWrites: Promise<void> = Promise.resolve();

  readonly session = this.sessionSignal.asReadonly();
  readonly cart = this.cartSignal.asReadonly();
  readonly total = computed(() => cartTotal(this.cartSignal()));
  readonly held = this.heldSignal.asReadonly();
  readonly lastSale = this.lastSaleSignal.asReadonly();
  readonly submitting = this.submittingSignal.asReadonly();

  async load(): Promise<void> {
    const journal = await this.journalService.get();
    this.sessionSignal.set(await journal.currentSession());
    this.cartSignal.set(await journal.loadDraft());
    this.heldSignal.set(await journal.listHeld());
  }

  /** Attend que le brouillon du panier soit écrit (tests, et avant toute opération qui le remplace). */
  settle(): Promise<void> {
    return this.draftWrites;
  }

  private setCart(update: (cart: CartLine[]) => CartLine[]): void {
    this.cartSignal.update(update);
    const snapshot = this.cartSignal();
    this.draftWrites = this.draftWrites
      .then(async () => (await this.journalService.get()).saveDraft(snapshot))
      .catch(() => undefined);
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
    this.setCart((cart) => addProduct(cart, product, quantity));
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
    this.setCart((cart) => setQuantity(cart, productId, quantity));
  }

  remove(productId: string): void {
    this.setCart((cart) => removeLine(cart, productId));
  }

  clearCart(): void {
    this.setCart(() => []);
    this.checkoutId = null;
  }

  // ---- Ventes en attente (PWA-10) -----------------------------------------------------------------

  /** Range le panier pour servir le client suivant. Ce n'est pas une vente : file d'envoi et stock restent intacts. */
  async holdCurrent(label?: string): Promise<void> {
    const lines = this.cartSignal();
    if (lines.length === 0) {
      throw new Error('Le panier est vide : il n’y a rien à mettre en attente.');
    }
    await this.draftWrites;
    const now = new Date();
    const time = `${String(now.getHours()).padStart(2, '0')}:${String(now.getMinutes()).padStart(2, '0')}`;
    const journal = await this.journalService.get();
    await journal.holdCart({
      id: crypto.randomUUID(),
      label: label?.trim() || `Vente de ${time}`,
      lines,
      createdAt: now.toISOString(),
    });
    this.cartSignal.set([]);
    this.checkoutId = null;
    this.heldSignal.set(await journal.listHeld());
  }

  /** Reprend une vente en attente : possible seulement si le panier courant est vide. */
  async resume(id: string): Promise<void> {
    if (this.cartSignal().length > 0) {
      throw new Error('Le panier en cours n’est pas vide : mettez-le en attente ou videz-le avant de reprendre.');
    }
    await this.draftWrites;
    const journal = await this.journalService.get();
    this.cartSignal.set(await journal.resumeHeld(id));
    this.checkoutId = null;
    this.heldSignal.set(await journal.listHeld());
  }

  async discard(id: string): Promise<void> {
    const journal = await this.journalService.get();
    await journal.deleteHeld(id);
    this.heldSignal.set(await journal.listHeld());
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
  confirmSale(payments: PaymentDraft[], customerId: string | null = null): Promise<SaleOperation> {
    if (this.inFlight) {
      return this.inFlight;
    }
    this.checkoutId ??= crypto.randomUUID();
    const input = { id: this.checkoutId, at: new Date().toISOString(), lines: this.cartSignal(), payments, customerId };
    this.submittingSignal.set(true);
    this.inFlight = (async () => {
      try {
        const sale = await (await this.journalService.get()).recordSale(input);
        this.lastSaleSignal.set(sale);
        this.setCart(() => []);
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
    this.heldSignal.set([]);
    void this.sync.requestFlush();
    return closing;
  }
}
