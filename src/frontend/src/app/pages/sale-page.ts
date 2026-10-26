import { Component, computed, DestroyRef, inject, OnInit, signal, viewChild } from '@angular/core';
import { formatFcfa } from '../core/money';
import { cartTotal } from '../domain/cart';
import { FamilyGroup, groupByFamily } from '../domain/families';
import { CartLine, CatalogueProduct } from '../domain/types';
import { CashService } from '../services/cash.service';
import { AuthService } from '../services/auth.service';
import { CatalogueService } from '../services/catalogue.service';
import { isEditable, ScanService } from '../services/scan.service';
import { SubscriptionService } from '../subscription/subscription.service';
import { ProductAvatar } from '../shared/product-avatar';
import { RouterLink } from '@angular/router';
import { ReceiptService } from '../services/receipt.service';
import { PaymentPanel } from './payment-panel';
import { TicketActions } from './ticket-actions';

const MAX_VISIBLE = 60;
/** En dessous de ce stock, le choix du format écrit « Plus que N » pour prévenir le caissier. */
const LOW_STOCK_HINT = 5;

/** S4 : catalogue (recherche, scan) et panier. Le total reste visible (DESIGN.md §4, §5). */
@Component({
  selector: 'app-sale-page',
  imports: [PaymentPanel, ProductAvatar, RouterLink, TicketActions],
  template: `
    <p class="sr-only" role="status" aria-live="polite">{{ announcement() }}</p>
    @if (scanNote(); as note) {
      <!-- Un nouvel élément à chaque scan : deux scans du même produit s'annoncent chacun. -->
      @for (item of [note]; track item.serial) {
        <p data-testid="scan-note" class="mb-3 rounded border border-feuille bg-surface p-2 font-semibold text-feuille" role="status">{{ item.text }}</p>
      }
    }

    @if (paying()) {
      <div class="mx-auto max-w-xl">
        <app-payment-panel [total]="cash.total()" (cancelled)="paying.set(false)" (finished)="endSale()" />
      </div>
    } @else {
      <div class="grid grid-cols-[minmax(0,1fr)] gap-4 pb-24 lg:grid-cols-[minmax(0,1fr)_26rem] lg:pb-0">
        <section aria-labelledby="catalogue-titre">
          <h2 id="catalogue-titre" class="sr-only">Catalogue</h2>

          @if (cash.held().length > 0) {
            <section class="panel mb-4" aria-labelledby="attente-titre">
              <h3 id="attente-titre" class="text-lg font-bold">Ventes en attente ({{ cash.held().length }})</h3>
              <ul class="mt-2 divide-y divide-sauge">
                @for (held of cash.held(); track held.id) {
                  <li class="flex flex-wrap items-center gap-2 py-2">
                    <span class="min-w-0 flex-1">
                      <span class="block font-semibold">{{ held.label }}</span>
                      <span class="amount block text-sm text-ardoise">{{ articleCount(held.lines) }} · {{ format(linesTotal(held.lines)) }}</span>
                    </span>
                    @if (confirmDelete() === held.id) {
                      <span class="w-full text-base font-semibold text-erreur sm:w-auto" role="alert">Supprimer cette vente en attente ?</span>
                      <button type="button" class="btn" (click)="confirmDelete.set(null)">Non, la garder</button>
                      <button type="button" class="btn-primary !bg-erreur !border-erreur" (click)="discard(held.id)">Oui, supprimer</button>
                    } @else {
                      <button type="button" class="btn-primary" [attr.aria-label]="'Reprendre la vente ' + held.label" (click)="resume(held.id)">Reprendre</button>
                      <button type="button" class="btn" [attr.aria-label]="'Supprimer la vente en attente ' + held.label" (click)="confirmDelete.set(held.id)">Supprimer</button>
                    }
                  </li>
                }
              </ul>
              @if (cash.cart().length > 0) {
                <p class="field-help">Pour reprendre une vente en attente, mettez d’abord le panier en cours en attente ou videz-le.</p>
              }
            </section>
          }

          @if (receipts.last(); as lastTicket) {
            @if (reprinting()) {
              <section class="panel mb-4" aria-labelledby="dernier-ticket-titre">
                <h3 id="dernier-ticket-titre" class="mb-2 text-lg font-bold">Dernier ticket</h3>
                <app-ticket-actions [receipt]="lastTicket" />
                <button type="button" class="btn mt-3" (click)="reprinting.set(false)">Fermer</button>
              </section>
            } @else {
              <button type="button" class="btn mb-3" (click)="reprinting.set(true)">Réimprimer le dernier ticket</button>
            }
          }

          <form (submit)="scan($event)" role="search">
            <label class="field-label" for="recherche">Rechercher ou scanner un code-barres</label>
            <input
              id="recherche"
              class="field"
              type="search"
              data-scan="search"
              autocomplete="off"
              enterkeyhint="done"
              [value]="term()"
              (input)="search($any($event.target).value)"
              aria-describedby="recherche-aide"
            />
            <p id="recherche-aide" class="field-help">Un code-barres exact s’ajoute au panier avec la touche Entrée.</p>
          </form>
          @if (unknownCode()) {
            <p class="field-error" role="alert">Aucun produit pour le code {{ shortCode(unknownCode()) }}.</p>
          }
          @if (catalogue.refreshFailed()) {
            <p class="mt-2 text-sm text-avertissement" role="note">
              Catalogue non mis à jour : les prix et stocks affichés sont ceux de la dernière synchronisation.
            </p>
          }

          @if (visible().length === 0) {
            <p class="panel mt-4 text-ardoise">
              @if (term()) {
                Aucun produit ne correspond à « {{ term() }} ».
              } @else {
                Le catalogue est vide. Demandez à votre gérant d’ajouter des produits, puis reconnectez-vous.
              }
            </p>
          } @else {
            <ul class="mt-4 grid grid-cols-1 gap-2 sm:grid-cols-2 xl:grid-cols-3">
              @for (family of visible(); track family.familyId) {
                <li class="flex flex-col gap-2">
                  <button
                    type="button"
                    class="btn h-full w-full justify-start gap-3 !py-3 text-left"
                    [attr.aria-expanded]="family.formats.length > 1 ? openFamily() === family.familyId : null"
                    (click)="choose(family)"
                  >
                    <app-product-avatar [name]="family.title" [photoId]="family.photoId" />
                    <span class="flex min-w-0 flex-1 flex-col items-start gap-1">
                      <span class="text-base font-semibold">{{ family.title }}</span>
                      @if (family.formats.length === 1) {
                        <span class="amount text-lg font-bold text-feuille">{{ format(family.formats[0].salePriceFcfa) }}</span>
                        @if (family.formats[0].tracksStock) {
                          <span class="amount text-sm font-normal" [class.text-erreur]="family.formats[0].quantityOnHand <= 0">
                            {{ family.formats[0].quantityOnHand <= 0 ? 'Rupture de stock' : 'Stock : ' + family.formats[0].quantityOnHand }}
                          </span>
                        }
                      } @else {
                        <span class="amount text-sm font-normal text-ardoise">
                          {{ family.formats.length }} formats · de {{ format(family.formats[0].salePriceFcfa) }} à {{ format(family.formats[family.formats.length - 1].salePriceFcfa) }}
                        </span>
                      }
                    </span>
                  </button>
                  @if (family.formats.length > 1 && openFamily() === family.familyId) {
                    <div class="panel flex flex-col gap-2 border-feuille" role="group" [attr.aria-label]="family.name + ' : choisissez le format'">
                      <p class="font-semibold">{{ family.name }} : quel format ?</p>
                      @for (item of family.formats; track item.id) {
                        <button
                          type="button"
                          class="btn w-full flex-wrap justify-between gap-x-3 !py-3 text-left"
                          [attr.aria-label]="item.name + ', ' + format(item.salePriceFcfa) + stockPhrase(item)"
                          (click)="addFormat(item)"
                        >
                          <span class="text-xl font-bold">{{ item.formatLabel }}</span>
                          <span class="amount text-lg font-bold text-feuille">{{ format(item.salePriceFcfa) }}</span>
                          @if (item.tracksStock) {
                            <span class="amount w-full text-sm font-normal" [class.text-erreur]="item.quantityOnHand <= 0">{{ stockText(item) }}</span>
                          }
                        </button>
                      }
                    </div>
                  }
                </li>
              }
            </ul>
            @if (hiddenCount() > 0) {
              <p class="field-help">{{ hiddenCount() }} autres produits : affinez la recherche.</p>
            }
          }
        </section>

        <section id="panier" class="panel h-fit scroll-mt-4 lg:sticky lg:top-4" aria-labelledby="panier-titre">
          <h2 id="panier-titre" class="text-xl font-bold">Panier</h2>
          @if (cash.cart().length === 0) {
            <p class="mt-2 text-base text-ardoise">Le panier est vide. Touchez un produit ou scannez un code-barres.</p>
          } @else {
            <ul class="mt-2 divide-y divide-sauge">
              @for (line of cash.cart(); track line.productId) {
                <li class="py-3">
                  <div class="flex items-baseline justify-between gap-2">
                    <span class="font-semibold">{{ line.name }}</span>
                    <span class="amount font-bold">{{ format(line.unitPriceFcfa * line.quantity) }}</span>
                  </div>
                  <div class="mt-2 flex items-center gap-2">
                    <button type="button" class="btn" [attr.aria-label]="'Diminuer la quantité de ' + line.name" [disabled]="line.quantity <= 1" (click)="cash.changeQuantity(line.productId, line.quantity - 1)">−</button>
                    <span class="amount w-10 text-center text-lg font-bold" aria-live="polite">{{ line.quantity }}</span>
                    <button type="button" class="btn" [attr.aria-label]="'Augmenter la quantité de ' + line.name" (click)="cash.changeQuantity(line.productId, line.quantity + 1)">+</button>
                    <span class="amount text-sm text-ardoise">× {{ format(line.unitPriceFcfa) }}</span>
                    <button type="button" class="btn ml-auto" [attr.aria-label]="'Retirer ' + line.name + ' du panier'" (click)="cash.remove(line.productId)">Retirer</button>
                  </div>
                </li>
              }
            </ul>
          }
          <div class="mt-4 border-t border-sauge pt-4">
            <p class="flex items-baseline justify-between text-xl font-bold">
              <span>Total</span>
              <span class="amount text-3xl">{{ format(cash.total()) }}</span>
            </p>
            <button type="button" class="btn-primary mt-4 w-full text-lg" [disabled]="cash.cart().length === 0 || suspended()" (click)="startPayment()">
              Encaisser
            </button>
            @if (suspended()) {
              <p class="field-help" role="status">
                @if (isOwner()) {
                  La vente est suspendue : l’abonnement est terminé. <a class="font-semibold underline" routerLink="/gestion/abonnement">Voir mon abonnement</a>
                } @else {
                  <strong>Vente suspendue.</strong> Prévenez votre responsable : l’abonnement de la boutique est à renouveler.
                }
              </p>
            }
            @if (cash.cart().length > 0) {
              @if (holding()) {
                <form class="mt-3" (submit)="hold($event)" novalidate>
                  <label class="field-label" for="attente-nom">Repère du client (facultatif)</label>
                  <input
                    id="attente-nom"
                    class="field"
                    type="text"
                    maxlength="40"
                    autocomplete="off"
                    [value]="holdLabel()"
                    (input)="holdLabel.set($any($event.target).value)"
                  />
                  <div class="mt-2 grid grid-cols-2 gap-2">
                    <button type="button" class="btn" (click)="holding.set(false)">Annuler</button>
                    <button type="submit" class="btn-primary">Mettre en attente</button>
                  </div>
                </form>
              } @else {
                <button type="button" class="btn mt-2 w-full" (click)="holding.set(true)">Mettre en attente</button>
                <button type="button" class="btn mt-2 w-full" (click)="cash.clearCart()">Vider le panier</button>
              }
            }
            @if (message()) {
              <p class="field-error" role="alert">{{ message() }}</p>
            }
          </div>
        </section>
      </div>

      <!-- Petites largeurs : le total et l'encaissement restent accessibles sans défiler (DESIGN.md §5). -->
      @if (cash.cart().length > 0) {
        <div class="fixed inset-x-0 bottom-0 z-10 border-t border-sauge bg-surface p-3 lg:hidden">
          <div class="mx-auto flex max-w-screen-xl items-center gap-3">
            <button type="button" class="min-w-0 flex-1 text-left" (click)="showCart()" aria-label="Voir le panier">
              <span class="block text-sm text-ardoise">{{ itemCount() }} article{{ itemCount() > 1 ? 's' : '' }} · voir le panier</span>
              <span class="amount block text-2xl font-bold">{{ format(cash.total()) }}</span>
            </button>
            <button type="button" class="btn-primary shrink-0 text-lg" [disabled]="suspended()" (click)="startPayment()">Encaisser</button>
          </div>
        </div>
      }
    }
  `,
})
export class SalePage implements OnInit {
  protected readonly cash = inject(CashService);
  protected readonly catalogue = inject(CatalogueService);
  private readonly subscription = inject(SubscriptionService);
  private readonly auth = inject(AuthService);
  protected readonly receipts = inject(ReceiptService);
  private readonly scanner = inject(ScanService);
  private readonly panel = viewChild(PaymentPanel);

  constructor() {
    // Lecteur de codes-barres (ADR 0015) : un scan est pris sur la page ou dans la zone de recherche, jamais dans un autre champ de saisie.
    const stop = this.scanner.listen({
      accept: (target) => !isEditable(target) || (target as HTMLElement).dataset['scan'] === 'search',
      onScan: (scan) => void this.onScan(scan.code),
    });
    inject(DestroyRef).onDestroy(stop);
  }

  /** Abonnement terminé (lecture seule) : on ne démarre plus de nouvel encaissement ; l'écran dit pourquoi et vers qui se tourner. */
  protected readonly suspended = this.subscription.readOnly;
  protected readonly isOwner = computed(() => this.auth.user()?.role === 'Owner');

  protected readonly term = signal('');
  protected readonly paying = signal(false);
  protected readonly reprinting = signal(false);
  protected readonly unknownCode = signal('');
  protected readonly announcement = signal('');
  protected readonly scanNote = signal<{ serial: number; text: string } | null>(null);
  private scanSerial = 0;
  private scanNoteTimer: ReturnType<typeof setTimeout> | undefined;
  protected readonly holding = signal(false);
  protected readonly holdLabel = signal('');
  protected readonly confirmDelete = signal<string | null>(null);
  protected readonly message = signal('');
  protected readonly openFamily = signal<string | null>(null);
  protected readonly families = computed(() => groupByFamily(this.catalogue.products()));
  protected readonly visible = computed(() => this.families().slice(0, MAX_VISIBLE));
  protected readonly itemCount = computed(() => this.cash.cart().reduce((sum, line) => sum + line.quantity, 0));
  protected readonly hiddenCount = computed(() => Math.max(this.families().length - MAX_VISIBLE, 0));
  protected format = formatFcfa;

  async ngOnInit(): Promise<void> {
    await this.catalogue.loadLocal();
    // Un produit ajouté depuis « Gestion » doit apparaître ici sans reconnexion : on recopie le catalogue à chaque ouverture de la vente.
    // La copie locale reste affichée pendant ce temps, et si le réseau manque elle reste utilisable (PWA-03).
    void this.catalogue.refresh();
  }

  protected async search(term: string): Promise<void> {
    this.term.set(term);
    this.openFamily.set(null);
    this.unknownCode.set('');
    await this.catalogue.loadLocal(term);
  }

  /** Touche Entrée : un code-barres exact ajoute le produit, sinon la liste filtrée reste affichée. */
  protected async scan(event: Event): Promise<void> {
    event.preventDefault();
    const code = this.term().trim();
    if (code === '') {
      return;
    }
    const product = await this.catalogue.findByBarcode(code);
    if (product) {
      this.add(product);
      this.scanner.feedback(true);
      await this.search('');
    } else if (/^\d{6,}$/.test(code)) {
      this.unknownCode.set(code);
      this.scanner.feedback(false);
    }
  }

  /** Un scan reconnu par sa vitesse : produit ajouté, ou code inconnu dit à voix haute et à l'écran. La zone de recherche est toujours vidée. */
  private async onScan(code: string): Promise<void> {
    if (this.paying()) {
      if (!this.panel()?.isDone()) {
        this.scanner.feedback(false);
        this.tell('Terminez ou annulez le paiement avant de scanner un article.');
        return;
      }
      // Le client suivant : sur l'écran du ticket, le premier article scanné referme la vente finie et commence la suivante.
      await this.endSale();
    }
    const product = await this.catalogue.findByBarcode(code);
    await this.search('');
    if (product) {
      this.add(product);
      this.scanner.feedback(true);
      this.tell(`${product.name} ajouté au panier`);
    } else {
      this.unknownCode.set(code);
      this.announcement.set(`Aucun produit pour le code ${this.shortCode(code)}`);
      this.scanner.feedback(false);
    }
  }

  /** Message visible après un scan (le son peut être coupé) ; chaque message est un nouvel élément, annoncé même s'il répète le précédent. */
  private tell(text: string): void {
    clearTimeout(this.scanNoteTimer);
    this.scanNote.set({ serial: ++this.scanSerial, text });
    this.scanNoteTimer = setTimeout(() => this.scanNote.set(null), 4000);
  }

  /** Le texte d'un QR code peut être long : on n'en montre que le début. */
  protected shortCode(code: string): string {
    return code.length > 30 ? `${code.slice(0, 30)}…` : code;
  }

  protected articleCount(lines: readonly CartLine[]): string {
    const count = lines.reduce((sum, line) => sum + line.quantity, 0);
    return `${count} article${count > 1 ? 's' : ''}`;
  }

  protected linesTotal(lines: readonly CartLine[]): number {
    return cartTotal(lines);
  }

  protected async hold(event: Event): Promise<void> {
    event.preventDefault();
    await this.run(async () => {
      await this.cash.holdCurrent(this.holdLabel());
      this.holding.set(false);
      this.holdLabel.set('');
      this.announcement.set('Vente mise en attente. Le panier est vide.');
    });
  }

  protected async resume(id: string): Promise<void> {
    await this.run(async () => {
      await this.cash.resume(id);
      this.announcement.set('Vente reprise.');
    });
  }

  protected async discard(id: string): Promise<void> {
    await this.run(async () => {
      await this.cash.discard(id);
      this.confirmDelete.set(null);
      this.announcement.set('Vente en attente supprimée.');
    });
  }

  /** Les erreurs de règle (panier non vide, vente introuvable) s'affichent à l'écran, jamais en silence. */
  private async run(action: () => Promise<void>): Promise<void> {
    this.message.set('');
    try {
      await action();
    } catch (error) {
      this.message.set(error instanceof Error ? error.message : 'L’opération a échoué.');
    }
  }

  protected startPayment(): void {
    if (!this.suspended()) {
      this.paying.set(true);
    }
  }

  protected showCart(): void {
    document.getElementById('panier')?.scrollIntoView({ behavior: 'smooth', block: 'start' });
  }

  /** Une famille à un seul format s'ajoute d'un toucher ; sinon la carte ouvre (ou ferme) le choix du format. */
  protected choose(family: FamilyGroup): void {
    if (family.formats.length === 1) {
      this.add(family.formats[0]);
    } else {
      this.openFamily.set(this.openFamily() === family.familyId ? null : family.familyId);
    }
  }

  protected addFormat(product: CatalogueProduct): void {
    this.add(product);
    this.openFamily.set(null);
  }

  /** Disponibilité écrite en toutes lettres, jamais par la couleur seule. */
  protected stockText(product: CatalogueProduct): string {
    if (product.quantityOnHand <= 0) {
      return 'Rupture de stock';
    }
    return product.quantityOnHand <= LOW_STOCK_HINT ? `Plus que ${product.quantityOnHand}` : `Stock : ${product.quantityOnHand}`;
  }

  protected stockPhrase(product: CatalogueProduct): string {
    return product.tracksStock ? ', ' + this.stockText(product) : '';
  }

  protected add(product: CatalogueProduct): void {
    this.cash.add(product);
    this.announcement.set(`${product.name} ajouté au panier`);
  }

  protected async endSale(): Promise<void> {
    this.paying.set(false);
    this.term.set('');
    await this.catalogue.loadLocal();
  }
}
