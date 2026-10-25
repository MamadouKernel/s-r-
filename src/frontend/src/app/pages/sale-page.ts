import { Component, computed, inject, OnInit, signal } from '@angular/core';
import { formatFcfa } from '../core/money';
import { cartTotal } from '../domain/cart';
import { CartLine, CatalogueProduct } from '../domain/types';
import { CashService } from '../services/cash.service';
import { CatalogueService } from '../services/catalogue.service';
import { PaymentPanel } from './payment-panel';

const MAX_VISIBLE = 60;

/** S4 : catalogue (recherche, scan) et panier. Le total reste visible (DESIGN.md §4, §5). */
@Component({
  selector: 'app-sale-page',
  imports: [PaymentPanel],
  template: `
    <p class="sr-only" role="status" aria-live="polite">{{ announcement() }}</p>

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

          <form (submit)="scan($event)" role="search">
            <label class="field-label" for="recherche">Rechercher ou scanner un code-barres</label>
            <input
              id="recherche"
              class="field"
              type="search"
              autocomplete="off"
              enterkeyhint="done"
              [value]="term()"
              (input)="search($any($event.target).value)"
              aria-describedby="recherche-aide"
            />
            <p id="recherche-aide" class="field-help">Un code-barres exact s’ajoute au panier avec la touche Entrée.</p>
          </form>
          @if (unknownCode()) {
            <p class="field-error" role="alert">Aucun produit pour le code {{ unknownCode() }}.</p>
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
              @for (product of visible(); track product.id) {
                <li class="flex">
                  <button type="button" class="btn h-full w-full flex-col items-start justify-start gap-1 !py-3 text-left" (click)="add(product)">
                    <span class="text-base font-semibold">{{ product.name }}</span>
                    <span class="amount text-lg font-bold text-feuille">{{ format(product.salePriceFcfa) }}</span>
                    @if (product.tracksStock) {
                      <span class="amount text-sm font-normal" [class.text-erreur]="product.quantityOnHand <= 0">
                        {{ product.quantityOnHand <= 0 ? 'Rupture de stock' : 'Stock : ' + product.quantityOnHand }}
                      </span>
                    }
                  </button>
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
            <button type="button" class="btn-primary mt-4 w-full text-lg" [disabled]="cash.cart().length === 0" (click)="paying.set(true)">
              Encaisser
            </button>
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
            <button type="button" class="btn-primary shrink-0 text-lg" (click)="paying.set(true)">Encaisser</button>
          </div>
        </div>
      }
    }
  `,
})
export class SalePage implements OnInit {
  protected readonly cash = inject(CashService);
  protected readonly catalogue = inject(CatalogueService);

  protected readonly term = signal('');
  protected readonly paying = signal(false);
  protected readonly unknownCode = signal('');
  protected readonly announcement = signal('');
  protected readonly holding = signal(false);
  protected readonly holdLabel = signal('');
  protected readonly confirmDelete = signal<string | null>(null);
  protected readonly message = signal('');
  protected readonly visible = computed(() => this.catalogue.products().slice(0, MAX_VISIBLE));
  protected readonly itemCount = computed(() => this.cash.cart().reduce((sum, line) => sum + line.quantity, 0));
  protected readonly hiddenCount = computed(() => Math.max(this.catalogue.products().length - MAX_VISIBLE, 0));
  protected format = formatFcfa;

  async ngOnInit(): Promise<void> {
    await this.catalogue.loadLocal();
    // Un produit ajouté depuis « Gestion » doit apparaître ici sans reconnexion : on recopie le catalogue à chaque ouverture de la vente.
    // La copie locale reste affichée pendant ce temps, et si le réseau manque elle reste utilisable (PWA-03).
    void this.catalogue.refresh();
  }

  protected async search(term: string): Promise<void> {
    this.term.set(term);
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
      await this.search('');
    } else if (/^\d{6,}$/.test(code)) {
      this.unknownCode.set(code);
    }
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

  protected showCart(): void {
    document.getElementById('panier')?.scrollIntoView({ behavior: 'smooth', block: 'start' });
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
