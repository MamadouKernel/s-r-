import { Component, computed, inject, OnInit, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { formatQuantity, MeasureUnit, parseQuantity, unitSymbol } from '../core/measure';
import { formatFcfa, parseFcfa } from '../core/money';
import { AuthService } from '../services/auth.service';
import { describeError } from './errors';
import { TeamApi } from './team-api';
import { ManagedProduct, MovementKind, ProductsApi, StockMovement } from './products-api';

const KIND_LABEL: Record<MovementKind, string> = {
  Reception: 'Réception',
  Sale: 'Vente',
  Loss: 'Perte',
  SaleCancellation: 'Annulation de vente',
};

/** GES-03 à GES-05 : réception, perte ou casse, historique des mouvements d'un produit. */
@Component({
  selector: 'app-product-stock-page',
  imports: [RouterLink],
  template: `
    <section aria-labelledby="stock-titre">
      <a class="btn mb-4" routerLink="/gestion/produits">← Retour aux produits</a>

      @if (loading()) {
        <p class="panel text-ardoise" role="status">Chargement…</p>
      } @else if (loadError()) {
        <p class="panel font-semibold text-erreur" role="alert">{{ loadError() }}</p>
      } @else if (product(); as current) {
        <h2 id="stock-titre" class="text-xl font-bold">{{ current.name }}</h2>
        <p class="amount mt-1 text-2xl font-bold" [class.text-erreur]="current.quantityOnHand <= 0">
          Stock : {{ quantity(current.quantityOnHand) }}
          @if (current.quantityOnHand <= 0) {
            <span class="text-base">· rupture de stock</span>
          } @else if (current.isLowStock) {
            <span class="text-base text-avertissement">· stock bas (seuil {{ quantity(current.alertThreshold) }})</span>
          }
        </p>

        <div role="status" aria-live="polite">
          @if (notice()) {
            <p class="panel mt-3 font-semibold text-succes">{{ notice() }}</p>
          }
        </div>

        <div class="mt-4 grid gap-4 lg:grid-cols-2">
          <form class="panel" (submit)="receive($event)" novalidate aria-labelledby="reception-titre">
            <h3 id="reception-titre" class="text-lg font-bold">Réception de marchandise</h3>
            <label class="field-label mt-3" for="reception-quantite">Quantité reçue{{ unitHint() }}</label>
            <input id="reception-quantite" class="field amount text-right" type="text" [attr.inputmode]="inputMode()" autocomplete="off" [value]="receiveQuantity()" (input)="receiveQuantity.set($any($event.target).value)" />
            <label class="field-label mt-3" for="reception-fournisseur">Fournisseur ou référence (facultatif)</label>
            <input id="reception-fournisseur" class="field" type="text" maxlength="500" autocomplete="off" [value]="supplier()" (input)="supplier.set($any($event.target).value)" />
            @if (current.purchasePriceFcfa !== null) {
              <label class="field-label mt-3" for="reception-prix">Prix d’achat de cette livraison (FCFA, facultatif)</label>
              <input id="reception-prix" class="field amount text-right" type="text" inputmode="numeric" autocomplete="off" [value]="newPurchasePrice()" (input)="newPurchasePrice.set($any($event.target).value)" aria-describedby="reception-prix-aide" />
              <p id="reception-prix-aide" class="field-help">Prix d’achat moyen actuel : {{ format(current.purchasePriceFcfa) }}. Il sera recalculé avec cette livraison.</p>
            }
            @if (receiveError()) {
              <p class="field-error" role="alert">{{ receiveError() }}</p>
            }
            <button type="submit" class="btn-primary mt-4 w-full" [disabled]="busy()">Enregistrer la réception</button>
          </form>

          <form class="panel" (submit)="loss($event)" novalidate aria-labelledby="perte-titre">
            <h3 id="perte-titre" class="text-lg font-bold">Perte ou casse</h3>
            <label class="field-label mt-3" for="perte-quantite">Quantité perdue{{ unitHint() }}</label>
            <input id="perte-quantite" class="field amount text-right" type="text" [attr.inputmode]="inputMode()" autocomplete="off" [value]="lossQuantity()" (input)="lossQuantity.set($any($event.target).value)" aria-describedby="perte-aide" />
            <p id="perte-aide" class="field-help">Au plus {{ quantity(current.quantityOnHand) }} (le stock disponible).</p>
            <label class="field-label mt-3" for="perte-motif">Motif (obligatoire)</label>
            <input id="perte-motif" class="field" type="text" maxlength="500" autocomplete="off" [value]="reason()" (input)="reason.set($any($event.target).value)" />
            <p class="field-help">Par exemple : périmé, cassé, consommation interne.</p>
            @if (lossError()) {
              <p class="field-error" role="alert">{{ lossError() }}</p>
            }
            <button type="submit" class="btn-primary mt-4 w-full" [disabled]="busy()">Enregistrer la perte</button>
          </form>
        </div>

        <section class="mt-6" aria-labelledby="historique-titre">
          <h3 id="historique-titre" class="mb-2 text-lg font-bold">Historique des mouvements</h3>
          @if (movements().length === 0) {
            <p class="panel text-ardoise">Aucun mouvement pour ce produit.</p>
          } @else {
            <!-- relative : sans cela, le texte masqué (sr-only, position absolue) échappe au défilement et élargit toute la page. -->
        <div class="relative overflow-x-auto rounded border border-sauge bg-surface">
              <table class="w-full min-w-[34rem] text-left text-base">
                <caption class="sr-only">Mouvements de stock, du plus récent au plus ancien</caption>
                <thead class="border-b border-sauge text-sm text-ardoise">
                  <tr>
                    <th scope="col" class="px-3 py-2">Date</th>
                    <th scope="col" class="px-3 py-2">Type</th>
                    <th scope="col" class="px-3 py-2 text-right">Quantité</th>
                    <th scope="col" class="px-3 py-2">Motif ou fournisseur</th>
                    <th scope="col" class="px-3 py-2">Auteur</th>
                  </tr>
                </thead>
                <tbody class="divide-y divide-sauge">
                  @for (movement of movements(); track movement.id) {
                    <tr>
                      <td class="px-3 py-2">{{ date(movement.at) }}</td>
                      <td class="px-3 py-2 font-semibold">{{ kind(movement) }}</td>
                      <td class="amount px-3 py-2 text-right font-bold">{{ movement.quantityDelta > 0 ? '+' : '' }}{{ quantity(movement.quantityDelta) }}</td>
                      <td class="px-3 py-2">
                        {{ movement.reason || '—' }}
                        @if (movement.unitCostFcfa != null) {
                          <span class="block text-sm text-ardoise">à {{ format(movement.unitCostFcfa) }} {{ costPer() }}</span>
                        }
                      </td>
                      <td class="px-3 py-2">{{ author(movement) }}</td>
                    </tr>
                  }
                </tbody>
              </table>
            </div>
            @if (names().size === 0) {
              <p class="field-help">Les noms des auteurs ne sont pas disponibles : leur identifiant est affiché.</p>
            }
          }
        </section>
      }
    </section>
  `,
})
export class ProductStockPage implements OnInit {
  private readonly api = inject(ProductsApi);
  private readonly route = inject(ActivatedRoute);
  private readonly auth = inject(AuthService);
  private readonly team = inject(TeamApi);

  /** Noms des auteurs (annuaire) ; vide si l'annuaire n'est pas disponible : l'identifiant court sert alors de repli. */
  protected readonly names = signal<ReadonlyMap<string, string>>(new Map());

  protected readonly product = signal<ManagedProduct | null>(null);
  protected readonly movements = signal<StockMovement[]>([]);
  protected readonly loading = signal(true);
  protected readonly loadError = signal('');
  protected readonly busy = signal(false);
  protected readonly notice = signal('');

  protected readonly receiveQuantity = signal('');
  protected readonly supplier = signal('');
  protected readonly newPurchasePrice = signal('');
  protected readonly receiveError = signal('');
  protected readonly lossQuantity = signal('');
  protected readonly reason = signal('');
  protected readonly lossError = signal('');

  /** Identifiants de mouvement créés une fois par formulaire et renouvelés après un succès : un rejeu ne double jamais un mouvement. */
  private receptionId = crypto.randomUUID();
  private lossId = crypto.randomUUID();

  protected readonly id = computed(() => this.route.snapshot.paramMap.get('id') ?? '');
  protected format = formatFcfa;

  protected readonly unit = computed<MeasureUnit>(() => this.product()?.unit ?? 'Piece');
  protected readonly unitHint = computed(() => (this.unit() === 'Piece' ? '' : ` (en ${unitSymbol(this.unit())})`));
  protected readonly inputMode = computed(() => (this.unit() === 'Piece' ? 'numeric' : 'decimal'));
  protected readonly costPer = computed(() => (this.unit() === 'Piece' ? 'l’unité' : `le ${unitSymbol(this.unit()) === 'kg' ? 'kilo' : 'litre'}`));

  /** Une quantité du produit, dans son unité : « 12,500 kg » pour un produit pesé, « 12 » pour des pièces. */
  protected quantity(value: number): string {
    return formatQuantity(value, this.unit());
  }

  async ngOnInit(): Promise<void> {
    await this.refresh();
    this.loading.set(false);
  }

  protected async receive(event: Event): Promise<void> {
    event.preventDefault();
    if (this.busy()) {
      return;
    }
    this.receiveError.set('');
    const parsed = parseQuantity(this.receiveQuantity(), this.unit());
    if (!parsed.ok) {
      this.receiveError.set(this.unit() === 'Piece' ? 'Saisissez la quantité reçue en chiffres entiers, supérieure à zéro.' : parsed.message);
      return;
    }
    const quantity = parsed.quantity;
    const priceText = this.newPurchasePrice().trim();
    const price = priceText === '' ? null : parseFcfa(priceText);
    if (priceText !== '' && price === null) {
      this.receiveError.set('Le prix d’achat de la livraison doit être un montant entier en FCFA.');
      return;
    }

    await this.run(
      async () => {
        const result = await this.api.receive(this.id(), {
          movementId: this.receptionId,
          quantity,
          supplier: this.supplier().trim() || null,
          purchasePriceFcfa: price,
        });
        this.receptionId = crypto.randomUUID();
        this.receiveQuantity.set('');
        this.supplier.set('');
        this.newPurchasePrice.set('');
        const average = result.purchasePriceFcfa;
        this.notice.set(
          `Réception enregistrée. Stock : ${this.quantity(result.quantityOnHand)}.` +
            (price !== null && average != null ? ` Prix d’achat moyen : ${formatFcfa(average)}.` : ''),
        );
      },
      (message) => this.receiveError.set(message),
    );
  }

  protected async loss(event: Event): Promise<void> {
    event.preventDefault();
    if (this.busy()) {
      return;
    }
    this.lossError.set('');
    const parsed = parseQuantity(this.lossQuantity(), this.unit());
    if (!parsed.ok) {
      this.lossError.set(this.unit() === 'Piece' ? 'Saisissez la quantité perdue en chiffres entiers, supérieure à zéro.' : parsed.message);
      return;
    }
    const quantity = parsed.quantity;
    if (this.reason().trim() === '') {
      this.lossError.set('Le motif de la perte est obligatoire.');
      return;
    }
    const available = this.product()?.quantityOnHand ?? 0;
    if (quantity > available) {
      this.lossError.set(`La perte dépasse le stock disponible (${this.quantity(available)}).`);
      return;
    }

    await this.run(
      async () => {
        const result = await this.api.recordLoss(this.id(), { movementId: this.lossId, quantity, reason: this.reason().trim() });
        this.lossId = crypto.randomUUID();
        this.lossQuantity.set('');
        this.reason.set('');
        this.notice.set(`Perte enregistrée. Stock : ${this.quantity(result.quantityOnHand)}.`);
      },
      (message) => this.lossError.set(message),
    );
  }

  protected author(movement: StockMovement): string {
    return this.names().get(movement.userId) ?? movement.userId.slice(0, 8);
  }

  protected kind(movement: StockMovement): string {
    return KIND_LABEL[movement.kind] ?? movement.kind;
  }

  protected date(iso: string): string {
    return new Date(iso).toLocaleString('fr-FR', { dateStyle: 'short', timeStyle: 'short' });
  }

  private async run(action: () => Promise<void>, onError: (message: string) => void): Promise<void> {
    this.busy.set(true);
    this.notice.set('');
    try {
      await action();
      await this.refresh();
    } catch (error) {
      onError(describeError(error));
    } finally {
      this.busy.set(false);
    }
  }

  private async loadNames(): Promise<void> {
    try {
      this.names.set(new Map((await this.team.directory()).map((entry) => [entry.id, entry.displayName])));
    } catch {
      this.names.set(new Map());
    }
  }

  private async refresh(): Promise<void> {
    try {
      const includeInactive = this.auth.user()?.role === 'Owner' || this.auth.user()?.role === 'Manager';
      const found = (await this.api.list({ includeInactive })).find((candidate) => candidate.id === this.id());
      if (!found) {
        this.loadError.set('Produit introuvable.');
        return;
      }
      this.product.set(found);
      this.movements.set(await this.api.movements(this.id(), 50));
      await this.loadNames();
    } catch (error) {
      this.loadError.set(describeError(error));
    }
  }
}
