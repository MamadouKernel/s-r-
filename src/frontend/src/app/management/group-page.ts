import { Component, computed, inject, OnInit, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { fold } from '../domain/families';
import { describeError } from './errors';
import { FlashService } from './flash.service';
import { ManagedProduct, ProductsApi } from './products-api';

/**
 * « Regrouper dans une famille » (docs/specs/formats-produits FMT-08) : des produits saisis séparément deviennent les formats d'une même famille.
 * Stocks, prix et ventes passées ne changent pas ; seul le nom affiché change, et l'écran le dit avant de confirmer.
 */
@Component({
  selector: 'app-group-page',
  imports: [RouterLink],
  template: `
    <section class="mx-auto max-w-2xl" aria-labelledby="regrouper-titre">
      <a class="btn mb-4" routerLink="/gestion/produits">← Retour aux produits</a>
      <h2 id="regrouper-titre" class="mb-4 text-xl font-bold">Regrouper dans une famille</h2>

      @if (loading()) {
        <p class="panel text-ardoise" role="status">Chargement des produits…</p>
      } @else if (loadError()) {
        <p class="panel font-semibold text-erreur" role="alert">{{ loadError() }}</p>
      } @else if (candidates().length < 2) {
        <p class="panel text-ardoise">Il faut au moins deux produits seuls pour en faire une famille.</p>
      } @else {
        <form class="panel" (submit)="save($event)" novalidate>
          <fieldset>
            <legend class="field-label">1. Quels produits regrouper ?</legend>
            @for (item of candidates(); track item.id) {
              <label class="flex min-h-12 items-center gap-3 text-base">
                <input type="checkbox" class="h-6 w-6" [checked]="isChosen(item.id)" (change)="toggle(item.id, $any($event.target).checked)" />
                <span>{{ item.name }} @if (item.tracksStock) { <span class="text-sm text-ardoise">(stock {{ item.quantityOnHand }})</span> }</span>
              </label>
            }
          </fieldset>
          @if (chosenError()) {
            <p class="field-error" role="alert">{{ chosenError() }}</p>
          }

          <label class="field-label mt-4" for="nom">2. Nom de la famille</label>
          <input id="nom" class="field" type="text" maxlength="200" autocomplete="off" [value]="name()" (input)="name.set($any($event.target).value)" aria-describedby="nom-erreur" />
          <p id="nom-erreur" class="field-error">{{ nameError() }}</p>

          <label class="field-label mt-2" for="categorie">Catégorie (facultatif)</label>
          <input id="categorie" class="field" type="text" maxlength="100" autocomplete="off" [value]="category()" (input)="category.set($any($event.target).value)" />

          @if (chosen().length > 0) {
            <fieldset class="mt-4">
              <legend class="field-label">3. Format de chaque produit</legend>
              @for (item of chosen(); track item.id; let index = $index) {
                <label class="mt-2 block text-sm text-ardoise" [attr.for]="'format-' + index">{{ item.name }} devient</label>
                <input
                  [id]="'format-' + index" class="field" type="text" maxlength="40" autocomplete="off" placeholder="1 L, 50 kg…"
                  [value]="labels()[item.id] ?? ''" (input)="setLabel(item.id, $any($event.target).value)"
                  [attr.aria-describedby]="'format-' + index + '-erreur'"
                />
                <p [id]="'format-' + index + '-erreur'" class="field-error">{{ labelErrors()[item.id] }}</p>
              }
            </fieldset>
          }

          @if (preview().length > 0) {
            <div class="mt-4 rounded border border-avertissement p-3" role="note">
              <p class="font-semibold">Avant de confirmer</p>
              <p>
                Dans la caisse, vous verrez
                @for (line of preview(); track line; let last = $last; let first = $first) {
                  {{ first ? '' : last ? ' et ' : ', ' }}« {{ line }} »
                }.
                Les anciennes ventes gardent leur ancien nom ; les stocks et les prix ne changent pas.
              </p>
            </div>
          }

          @if (failure()) {
            <p class="field-error mt-4" role="alert">{{ failure() }}</p>
          }
          <div class="mt-4 grid grid-cols-2 gap-3">
            <a class="btn" routerLink="/gestion/produits">Annuler</a>
            <button type="submit" class="btn-primary" [disabled]="saving()">
              {{ saving() ? 'Regroupement…' : 'Regrouper ces ' + chosen().length + ' produits' }}
            </button>
          </div>
        </form>
      }
    </section>
  `,
})
export class GroupPage implements OnInit {
  private readonly api = inject(ProductsApi);
  private readonly router = inject(Router);
  private readonly flash = inject(FlashService);

  /** Identifiant fixé à l'ouverture : un nouvel essai après une coupure ne crée pas deux familles. */
  private readonly familyId = crypto.randomUUID();

  protected readonly loading = signal(true);
  protected readonly loadError = signal('');
  protected readonly products = signal<ManagedProduct[]>([]);
  protected readonly chosenIds = signal<string[]>([]);
  protected readonly name = signal('');
  protected readonly category = signal('');
  protected readonly labels = signal<Record<string, string>>({});
  protected readonly nameError = signal('');
  protected readonly chosenError = signal('');
  protected readonly labelErrors = signal<Record<string, string>>({});
  protected readonly failure = signal('');
  protected readonly saving = signal(false);

  /** Les produits seuls dans leur famille : ceux d'une famille de plusieurs formats ne se regroupent pas une seconde fois. */
  protected readonly candidates = computed(() => {
    const sizes = new Map<string, number>();
    for (const item of this.products()) {
      const key = item.familyId ?? item.id;
      sizes.set(key, (sizes.get(key) ?? 0) + 1);
    }
    return this.products().filter((item) => item.isActive && sizes.get(item.familyId ?? item.id) === 1);
  });
  protected readonly chosen = computed(() => this.candidates().filter((item) => this.chosenIds().includes(item.id)));
  protected readonly preview = computed(() => {
    const family = this.name().trim();
    return family === '' ? [] : this.chosen().map((item) => `${family} · ${this.labels()[item.id]?.trim() || '…'}`);
  });

  async ngOnInit(): Promise<void> {
    try {
      this.products.set(await this.api.list());
    } catch (error) {
      this.loadError.set(describeError(error));
    } finally {
      this.loading.set(false);
    }
  }

  protected isChosen(id: string): boolean {
    return this.chosenIds().includes(id);
  }

  protected toggle(id: string, checked: boolean): void {
    this.chosenIds.update((ids) => (checked ? [...ids, id] : ids.filter((other) => other !== id)));
  }

  protected setLabel(id: string, value: string): void {
    this.labels.update((labels) => ({ ...labels, [id]: value }));
  }

  protected async save(event: Event): Promise<void> {
    event.preventDefault();
    if (this.saving()) {
      return;
    }
    this.failure.set('');
    const chosen = this.chosen();
    const name = this.name().trim();
    this.chosenError.set(chosen.length < 2 ? 'Choisissez au moins deux produits.' : '');
    this.nameError.set(name === '' ? 'Le nom de la famille est obligatoire.' : '');
    const errors: Record<string, string> = {};
    const seen = new Set<string>();
    const members = chosen.map((item) => {
      const label = (this.labels()[item.id] ?? '').split(/\s+/).filter(Boolean).join(' ');
      if (label === '') {
        errors[item.id] = 'Donnez un format à ce produit (1 L, 50 kg…).';
      } else if (seen.has(fold(label))) {
        errors[item.id] = `Le format « ${label} » est déjà pris par un autre produit de la liste.`;
      }
      seen.add(fold(label));
      return { productId: item.id, label };
    });
    this.labelErrors.set(errors);
    if (this.chosenError() || this.nameError() || Object.keys(errors).length > 0) {
      return;
    }

    this.saving.set(true);
    try {
      await this.api.group({ familyId: this.familyId, name, category: this.category().trim() || null, members });
      this.flash.show('Produits regroupés.');
      await this.router.navigateByUrl('/gestion/produits');
    } catch (error) {
      this.failure.set(describeError(error));
    } finally {
      this.saving.set(false);
    }
  }
}
