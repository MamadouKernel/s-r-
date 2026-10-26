import { Component, computed, inject, OnInit, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { formatQuantity, perUnit } from '../core/measure';
import { formatFcfa } from '../core/money';
import { AuthService } from '../services/auth.service';
import { NetworkService } from '../services/network.service';
import { describeError } from './errors';
import { FlashService } from './flash.service';
import { ManagedProduct, ProductsApi } from './products-api';
import { canManageCatalogue } from './roles';
import { ProductAvatar } from '../shared/product-avatar';

/** GES-01 : liste des produits, recherche, stock bas. En ligne seulement. */
@Component({
  selector: 'app-products-page',
  imports: [RouterLink, ProductAvatar],
  template: `
    <section aria-labelledby="produits-titre">
      <div class="mb-4 flex flex-wrap items-center gap-3">
        <h2 id="produits-titre" class="text-xl font-bold">Produits</h2>
        @if (canCreate()) {
          <a class="btn ml-auto" routerLink="/gestion/produits/importer">Importer depuis Excel</a>
          <a class="btn" routerLink="/gestion/produits/regrouper">Regrouper dans une famille</a>
          <a class="btn-primary" routerLink="/gestion/produits/nouveau">Nouveau produit</a>
        }
      </div>

      @if (flash()) {
        <p class="panel mb-4 font-semibold text-succes" role="status">{{ flash() }}</p>
      }
      @if (!network.online()) {
        <p class="panel mb-4 font-semibold text-avertissement" role="note">
          Vous êtes hors ligne : cet écran a besoin du réseau. La caisse, elle, continue de fonctionner.
        </p>
      }

      <form class="mb-4 grid gap-3 sm:grid-cols-[minmax(0,1fr)_auto]" (submit)="search($event)" role="search">
        <div>
          <label class="field-label" for="recherche-produit">Rechercher par nom ou code-barres</label>
          <input id="recherche-produit" class="field" type="search" autocomplete="off" [value]="term()" (input)="term.set($any($event.target).value)" />
        </div>
        <button type="submit" class="btn-primary self-end">Rechercher</button>
      </form>

      <div class="mb-4 flex flex-wrap gap-x-6 gap-y-2">
        <label class="flex items-center gap-2 text-base">
          <input type="checkbox" class="h-6 w-6" [checked]="lowOnly()" (change)="toggleLowOnly($any($event.target).checked)" />
          Stock bas seulement
        </label>
        @if (canCreate()) {
          <label class="flex items-center gap-2 text-base">
            <input type="checkbox" class="h-6 w-6" [checked]="includeInactive()" (change)="toggleInactive($any($event.target).checked)" />
            Afficher les produits désactivés
          </label>
        }
      </div>

      @if (loading()) {
        <p class="panel text-ardoise" role="status">Chargement des produits…</p>
      } @else if (error()) {
        <div class="panel" role="alert">
          <p class="font-semibold text-erreur">{{ error() }}</p>
          <button type="button" class="btn mt-3" (click)="load()">Réessayer</button>
        </div>
      } @else if (products().length === 0) {
        <p class="panel text-ardoise">
          @if (lowOnly()) {
            Aucun produit sous son seuil d’alerte : le stock est correct.
          } @else if (activeSearch()) {
            Aucun produit ne correspond à « {{ activeSearch() }} ».
          } @else {
            Le catalogue est vide.
            @if (canCreate()) {
              Créez votre premier produit avec « Nouveau produit ».
            }
          }
        </p>
      } @else {
        <!-- relative : sans cela, le texte masqué (sr-only, position absolue) échappe au défilement et élargit toute la page. -->
        <div class="relative overflow-x-auto rounded border border-sauge bg-surface">
          <table class="w-full min-w-[40rem] text-left text-base">
            <caption class="sr-only">Liste des produits</caption>
            <thead class="border-b border-sauge text-sm text-ardoise">
              <tr>
                <th scope="col" class="px-3 py-2">Produit</th>
                <th scope="col" class="px-3 py-2 text-right">Prix de vente</th>
                @if (showPurchase()) {
                  <th scope="col" class="px-3 py-2 text-right">Prix d’achat</th>
                }
                <th scope="col" class="px-3 py-2 text-right">Stock</th>
                <th scope="col" class="px-3 py-2">État</th>
                <th scope="col" class="px-3 py-2"><span class="sr-only">Actions</span></th>
              </tr>
            </thead>
            @for (family of families(); track family.familyId) {
              <tbody class="divide-y divide-sauge border-t border-sauge">
                @if (family.formats.length > 1) {
                  <tr class="bg-ivoire">
                    <th scope="colgroup" [attr.colspan]="columns()" class="px-3 py-2 text-left">
                      <span class="flex items-center gap-3">
                        <app-product-avatar [name]="family.name" [photoId]="family.photoId" />
                        <span class="min-w-0 flex-1">
                          <span class="block font-semibold">{{ family.name }}</span>
                          <span class="block text-sm font-normal text-ardoise">
                            {{ family.category ? family.category + ' · ' : '' }}{{ family.formats.length }} formats
                          </span>
                        </span>
                        @if (canCreate()) {
                          <a class="btn" [routerLink]="['/gestion/produits/famille', family.familyId]" [attr.aria-label]="'Famille ' + family.name">Modifier la famille</a>
                        }
                      </span>
                    </th>
                  </tr>
                }
                @for (product of family.formats; track product.id) {
                  <tr>
                    <th scope="row" class="px-3 py-3 font-semibold" [class.pl-8]="family.formats.length > 1">
                      @if (family.formats.length > 1) {
                        <span class="sr-only">{{ family.name }} · </span>{{ product.formatLabel ?? product.name }}
                      } @else {
                        {{ product.name }}
                        @if (product.category) {
                          <span class="block text-sm font-normal text-ardoise">{{ product.category }}</span>
                        }
                      }
                    </th>
                    <td class="amount px-3 py-3 text-right">{{ format(product.salePriceFcfa) }}{{ perUnit(product.unit) }}</td>
                    @if (showPurchase()) {
                      <td class="amount px-3 py-3 text-right">{{ product.purchasePriceFcfa === null ? '—' : format(product.purchasePriceFcfa) }}</td>
                    }
                    <td class="amount px-3 py-3 text-right">{{ product.tracksStock ? quantity(product) : '—' }}</td>
                    <td class="px-3 py-3">
                      <span class="font-semibold" [class.text-erreur]="stockState(product) === 'Rupture de stock'" [class.text-avertissement]="stockState(product) === 'Stock bas'">
                        {{ stockState(product) }}
                      </span>
                      @if (!product.isActive) {
                        <span class="block text-sm text-ardoise">Désactivé</span>
                      }
                    </td>
                    <td class="px-3 py-3">
                      <div class="flex flex-wrap justify-end gap-2">
                        @if (canCreate()) {
                          <a class="btn" [routerLink]="['/gestion/produits', product.id]" [attr.aria-label]="'Modifier ' + product.name">Modifier</a>
                          @if (family.formats.length === 1) {
                            <a class="btn" [routerLink]="['/gestion/produits/famille', family.familyId]" [attr.aria-label]="'Formats de ' + product.name">Formats</a>
                          }
                        }
                        @if (product.tracksStock) {
                          <a class="btn" [routerLink]="['/gestion/produits', product.id, 'stock']" [attr.aria-label]="'Stock de ' + product.name">Stock</a>
                        }
                      </div>
                    </td>
                  </tr>
                }
              </tbody>
            }
          </table>
        </div>
      }
    </section>
  `,
})
export class ProductsPage implements OnInit {
  private readonly api = inject(ProductsApi);
  private readonly auth = inject(AuthService);
  private readonly flashService = inject(FlashService);
  protected readonly network = inject(NetworkService);

  protected readonly term = signal('');
  protected readonly activeSearch = signal('');
  protected readonly lowOnly = signal(false);
  protected readonly includeInactive = signal(false);
  protected readonly products = signal<ManagedProduct[]>([]);
  protected readonly loading = signal(true);
  protected readonly error = signal('');
  protected readonly flash = signal('');

  protected readonly canCreate = computed(() => canManageCatalogue(this.auth.user()?.role));
  /** Le serveur n'envoie le prix d'achat qu'aux profils autorisés : la colonne n'apparaît que s'il y a des valeurs. */
  protected readonly families = computed(() => groupManaged(this.products()));
  protected readonly columns = computed(() => (this.showPurchase() ? 6 : 5));
  protected readonly showPurchase = computed(() => this.products().some((product) => product.purchasePriceFcfa !== null));
  protected format = formatFcfa;
  protected perUnit = perUnit;

  protected quantity(product: ManagedProduct): string {
    return formatQuantity(product.quantityOnHand, product.unit);
  }

  async ngOnInit(): Promise<void> {
    this.flash.set(this.flashService.consume());
    await this.load();
  }

  protected async search(event: Event): Promise<void> {
    event.preventDefault();
    this.activeSearch.set(this.term().trim());
    await this.load();
  }

  protected async toggleLowOnly(checked: boolean): Promise<void> {
    this.lowOnly.set(checked);
    await this.load();
  }

  protected async toggleInactive(checked: boolean): Promise<void> {
    this.includeInactive.set(checked);
    await this.load();
  }

  protected async load(): Promise<void> {
    this.loading.set(true);
    this.error.set('');
    try {
      this.products.set(
        this.lowOnly()
          ? await this.api.lowStock()
          : await this.api.list({ search: this.activeSearch(), includeInactive: this.includeInactive() }),
      );
    } catch (error) {
      this.products.set([]);
      this.error.set(describeError(error));
    } finally {
      this.loading.set(false);
    }
  }

  /** L'état du stock en toutes lettres : la couleur seule ne porte jamais le sens (DESIGN.md §7). */
  protected stockState(product: ManagedProduct): string {
    if (!product.tracksStock) {
      return 'Sans suivi de stock';
    }
    if (product.quantityOnHand <= 0) {
      return 'Rupture de stock';
    }
    return product.isLowStock ? 'Stock bas' : 'Stock correct';
  }
}

interface ManagedFamily {
  familyId: string;
  name: string;
  category: string | null;
  photoId: string | null;
  /** Du plus petit prix au plus grand. */
  formats: ManagedProduct[];
}

/** Regroupe la liste par famille, dans l'ordre où chaque famille apparaît ; un produit sans famille est sa propre famille. */
function groupManaged(products: readonly ManagedProduct[]): ManagedFamily[] {
  const groups = new Map<string, ManagedProduct[]>();
  for (const product of products) {
    const key = product.familyId ?? product.id;
    groups.set(key, [...(groups.get(key) ?? []), product]);
  }
  return [...groups].map(([familyId, members]) => {
    const formats = [...members].sort((a, b) => a.salePriceFcfa - b.salePriceFcfa);
    return {
      familyId,
      name: formats[0].familyName ?? formats[0].name,
      category: formats[0].category,
      photoId: formats.find((format) => format.photoId)?.photoId ?? null,
      formats,
    };
  });
}
