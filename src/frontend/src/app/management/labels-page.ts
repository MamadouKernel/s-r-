import { Component, computed, ElementRef, inject, OnInit, signal, viewChild } from '@angular/core';
import { DomSanitizer } from '@angular/platform-browser';
import { AuthService } from '../services/auth.service';
import { NetworkService } from '../services/network.service';
import { describeError } from './errors';
import { blockedProducts, clampCopies, clampStart, LABEL_FORMATS, LabelFormatId, labelsDocument, LabelSelection, planLabels } from './labels';
import { canManageCatalogue } from './roles';
import { ManagedProduct, ProductsApi } from './products-api';

/**
 * Étiquettes de produits (docs/specs/etiquettes-tickets ETQ-04) : on choisit des produits et un nombre d'exemplaires, on voit l'aperçu de la page
 * exacte qui sera imprimée, puis la fenêtre d'impression de l'appareil prend le relais (toute imprimante convient). Un produit sans code-barres
 * reçoit son code interne sur place ; tant qu'il en reste un, l'impression est refusée.
 */
@Component({
  selector: 'app-labels-page',
  template: `
    <section aria-labelledby="etiquettes-titre">
      <h2 id="etiquettes-titre" class="mb-4 text-xl font-bold">Étiquettes</h2>

      @if (notice()) {
        <p class="panel mb-4 font-semibold text-succes" role="status">{{ notice() }}</p>
      }
      @if (!network.online()) {
        <p class="panel mb-4 font-semibold text-avertissement" role="note">
          Vous êtes hors ligne : cet écran a besoin du réseau pour lister les produits. La caisse, elle, continue de fonctionner.
        </p>
      }
      @if (actionError()) {
        <p class="panel mb-4 font-semibold text-erreur" role="alert">{{ actionError() }}</p>
      }

      @if (loading()) {
        <p class="panel text-ardoise" role="status">Chargement des produits…</p>
      } @else if (error()) {
        <div class="panel" role="alert">
          <p class="font-semibold text-erreur">{{ error() }}</p>
          <button type="button" class="btn mt-3" (click)="load()">Réessayer</button>
        </div>
      } @else if (products().length === 0) {
        <p class="panel text-ardoise">Aucun produit pour le moment. Créez d’abord vos produits dans « Produits et stock ».</p>
      } @else {
        <div class="panel mb-4">
          <div class="flex flex-wrap items-end gap-3">
            <div class="min-w-0 flex-1">
              <label class="field-label" for="recherche-produit">Chercher un produit</label>
              <input id="recherche-produit" class="field" type="search" autocomplete="off" [value]="search()" (input)="search.set($any($event.target).value)" />
            </div>
            <button type="button" class="btn" (click)="selectAll()">Tout sélectionner</button>
            <button type="button" class="btn" (click)="selectNone()">Tout désélectionner</button>
          </div>
          <label class="mt-3 flex min-h-11 items-center gap-2">
            <input id="sans-code" type="checkbox" class="h-5 w-5" [checked]="onlyWithoutCode()" (change)="onlyWithoutCode.set($any($event.target).checked)" />
            Sans code-barres seulement
          </label>
        </div>

        <ul class="mb-4 flex flex-col gap-2">
          @for (product of visible(); track product.id) {
            <li class="panel flex flex-wrap items-center gap-3">
              <input type="checkbox" class="h-6 w-6" [attr.aria-label]="'Choisir ' + product.name + formatSuffix(product)" [checked]="isChosen(product)" (change)="toggle(product, $any($event.target).checked)" />
              <div class="min-w-0 flex-1">
                <p class="break-words font-semibold">{{ product.name }}@if (product.formatLabel) { <span class="font-normal text-ardoise"> · {{ product.formatLabel }}</span> }</p>
                <p class="text-sm text-ardoise">
                  <span class="amount">{{ price(product) }}</span> ·
                  @if (product.barcode) {
                    <span class="font-mono">{{ product.barcode }}</span>
                  } @else {
                    <span class="font-semibold text-avertissement">Pas de code-barres</span>
                  }
                </p>
              </div>
              @if (isChosen(product)) {
                <label class="flex items-center gap-2 text-sm">
                  Exemplaires
                  <input class="field !min-h-11 !w-20 text-center" type="text" inputmode="numeric" autocomplete="off" [attr.aria-label]="'Nombre d’étiquettes pour ' + product.name + formatSuffix(product)" [value]="copiesOf(product)" (input)="setCopies(product, $any($event.target).value)" />
                </label>
                @if (!product.barcode && canCreate()) {
                  <button type="button" class="btn" [attr.aria-label]="'Créer un code interne pour ' + product.name + formatSuffix(product)" [disabled]="busyId() === product.id" (click)="createCode(product)">Créer un code interne</button>
                }
              }
            </li>
          }
          @if (visible().length === 0) {
            <li class="panel text-ardoise">Aucun produit ne correspond.</li>
          }
        </ul>

        <div class="panel mb-4">
          <div class="flex flex-wrap items-end gap-3">
            <div>
              <label class="field-label" for="format">Format</label>
              <select id="format" class="field" (change)="format.set($any($event.target).value)">
                @for (option of formats; track option.id) {
                  <option [value]="option.id" [selected]="option.id === format()">{{ option.name }}</option>
                }
              </select>
            </div>
            @if (format() === 'a4') {
              <div>
                <label class="field-label" for="premiere">Commencer à l’étiquette n°</label>
                <input id="premiere" class="field !w-24 text-center" type="text" inputmode="numeric" autocomplete="off" [value]="startAt()" (input)="setStart($any($event.target).value)" />
              </div>
            }
            <button type="button" class="btn-primary ml-auto" [disabled]="!canPrint()" (click)="print()">Imprimer</button>
          </div>

          <p class="mt-3 font-semibold" role="status">{{ summary() }}</p>
          @if (blocked().length > 0) {
            <p class="mt-2 font-semibold text-avertissement">
              {{ blocked().length }} {{ blocked().length > 1 ? 'produits n’ont' : 'produit n’a' }} pas de code-barres.
              {{ canCreate() ? 'Créez-leur un code interne.' : 'Demandez à un gérant de leur créer un code interne.' }}
            </p>
          }
        </div>
      }

      @if (pages().length > 0) {
        <iframe #preview title="Aperçu des étiquettes" class="h-[32rem] w-full rounded border border-sauge bg-surface" [srcdoc]="document()"></iframe>
      }
    </section>
  `,
})
export class LabelsPage implements OnInit {
  private readonly api = inject(ProductsApi);
  private readonly auth = inject(AuthService);
  private readonly sanitizer = inject(DomSanitizer);
  protected readonly network = inject(NetworkService);
  private readonly preview = viewChild<ElementRef<HTMLIFrameElement>>('preview');

  protected readonly formats = Object.values(LABEL_FORMATS);
  protected readonly products = signal<ManagedProduct[]>([]);
  protected readonly loading = signal(true);
  protected readonly error = signal('');
  protected readonly notice = signal('');
  protected readonly actionError = signal('');
  protected readonly busyId = signal<string | null>(null);

  protected readonly search = signal('');
  protected readonly onlyWithoutCode = signal(false);
  protected readonly chosen = signal<Record<string, number>>({});
  protected readonly format = signal<LabelFormatId>('a4');
  protected readonly startAt = signal(1);

  protected readonly canCreate = computed(() => canManageCatalogue(this.auth.user()?.role));

  protected readonly visible = computed(() => {
    const term = this.search().trim().toLocaleLowerCase('fr');
    return this.products().filter(
      (product) =>
        (!this.onlyWithoutCode() || !(product.barcode ?? '').trim()) &&
        (term === '' || `${product.name} ${product.formatLabel ?? ''}`.toLocaleLowerCase('fr').includes(term)),
    );
  });

  protected readonly selection = computed<LabelSelection[]>(() =>
    this.products()
      .filter((product) => product.id in this.chosen())
      .map((product) => ({ product, copies: this.chosen()[product.id] })),
  );
  protected readonly blocked = computed(() => blockedProducts(this.selection()));
  protected readonly pages = computed(() => planLabels(this.selection(), this.format(), this.startAt()));
  protected readonly total = computed(() => this.selection().reduce((sum, item) => sum + item.copies, 0));
  protected readonly canPrint = computed(() => this.total() > 0 && this.blocked().length === 0);
  protected readonly document = computed(() => this.sanitizer.bypassSecurityTrustHtml(labelsDocument(this.pages(), this.format())));

  protected readonly summary = computed(() => {
    const count = this.total();
    if (count === 0) {
      return 'Aucune étiquette choisie : cochez des produits.';
    }
    const labels = `${count} étiquette${count > 1 ? 's' : ''}`;
    if (this.format() === 'roll') {
      return `${labels} (1 par page)`;
    }
    const sheets = this.pages().length;
    return `${labels} sur ${sheets} planche${sheets > 1 ? 's' : ''}`;
  });

  ngOnInit(): void {
    void this.load();
  }

  protected async load(): Promise<void> {
    this.loading.set(true);
    this.error.set('');
    try {
      this.products.set(await this.api.list());
    } catch (error) {
      this.products.set([]);
      this.error.set(describeError(error));
    } finally {
      this.loading.set(false);
    }
  }

  protected isChosen(product: ManagedProduct): boolean {
    return product.id in this.chosen();
  }

  protected copiesOf(product: ManagedProduct): number {
    return this.chosen()[product.id] ?? 1;
  }

  protected formatSuffix(product: ManagedProduct): string {
    return product.formatLabel ? ` ${product.formatLabel}` : '';
  }

  protected price(product: ManagedProduct): string {
    return `${product.salePriceFcfa.toString().replace(/\B(?=(\d{3})+(?!\d))/g, ' ')} FCFA`;
  }

  protected toggle(product: ManagedProduct, checked: boolean): void {
    this.chosen.update((current) => {
      const next = { ...current };
      if (checked) {
        next[product.id] = current[product.id] ?? 1;
      } else {
        delete next[product.id];
      }
      return next;
    });
  }

  protected selectAll(): void {
    this.chosen.update((current) => ({ ...current, ...Object.fromEntries(this.visible().map((product) => [product.id, current[product.id] ?? 1])) }));
  }

  protected selectNone(): void {
    this.chosen.set({});
  }

  protected setCopies(product: ManagedProduct, value: string): void {
    this.chosen.update((current) => ({ ...current, [product.id]: clampCopies(Number(value.replace(/\D/g, ''))) }));
  }

  protected setStart(value: string): void {
    this.startAt.set(clampStart(Number(value.replace(/\D/g, '')), LABEL_FORMATS.a4.columns * LABEL_FORMATS.a4.rows));
  }

  protected async createCode(product: ManagedProduct): Promise<void> {
    if (this.busyId()) {
      return;
    }
    this.busyId.set(product.id);
    this.actionError.set('');
    this.notice.set('');
    try {
      const barcode = await this.api.createInternalBarcode(product.id);
      this.products.update((list) => list.map((item) => (item.id === product.id ? { ...item, barcode } : item)));
      this.notice.set(`Code interne créé pour ${product.name}${this.formatSuffix(product)}.`);
    } catch (error) {
      this.actionError.set(describeError(error));
    } finally {
      this.busyId.set(null);
    }
  }

  protected print(): void {
    this.preview()?.nativeElement.contentWindow?.print();
  }
}
