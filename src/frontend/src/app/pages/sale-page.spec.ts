import 'fake-indexeddb/auto';
import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { provideRouter } from '@angular/router';
import { Journal } from '../data/journal';
import { typeScan } from '../core/scan-test-support';
import { CatalogueProduct } from '../domain/types';
import { ApiService } from '../services/api.service';
import { AuthService } from '../services/auth.service';
import { CashService } from '../services/cash.service';
import { CatalogueService } from '../services/catalogue.service';
import { JournalService } from '../services/journal.service';
import { SyncService } from '../services/sync.service';
import { SubscriptionService } from '../subscription/subscription.service';
import { ClosePage } from './close-page';
import { ReceiptService } from '../services/receipt.service';
import { SaleOperation } from '../data/journal';
import { PaymentPanel } from './payment-panel';
import { SalePage } from './sale-page';

const jus: CatalogueProduct = {
  id: 'p-jus', name: 'Jus', barcode: '111', category: null, salePriceFcfa: 600, tracksStock: true, quantityOnHand: 24,
};

async function setup(api: Partial<ApiService> = {}, catalogue: CatalogueProduct[] = [jus], role = 'Cashier', readOnly = signal(false)) {
  const journal = await Journal.open(`soro-ui-${Math.random()}`);
  await journal.replaceCatalogue(catalogue);
  TestBed.configureTestingModule({
    providers: [
      provideRouter([]),
      { provide: JournalService, useValue: { get: () => Promise.resolve(journal) } },
      { provide: SyncService, useValue: { requestFlush: vi.fn().mockResolvedValue(null), counts: () => ({ pending: 0, synced: 0, rejected: 0 }) } },
      { provide: ApiService, useValue: api },
      { provide: AuthService, useValue: { device: () => ({ tenantId: 't', registerId: 'r', pointOfSaleId: 'p' }), user: () => ({ id: 'u', displayName: 'Awa', role }) } },
      { provide: SubscriptionService, useValue: { readOnly } },
    ],
  });
  const cash = TestBed.inject(CashService);
  await TestBed.inject(CatalogueService).loadLocal();
  await cash.openSession(10000);
  return { cash, journal };
}

function helpers(root: HTMLElement, stable: () => Promise<unknown>) {
  const button = (label: string) =>
    [...root.querySelectorAll('button')].find((candidate) => (candidate.textContent ?? '').includes(label) || candidate.getAttribute('aria-label')?.includes(label));
  return {
    click: async (label: string) => {
      const target = button(label);
      expect(target, `bouton « ${label} » introuvable`).toBeTruthy();
      target!.click();
      await stable();
      await new Promise((resolve) => setTimeout(resolve, 10));
      await stable();
    },
    has: (label: string) => button(label) !== undefined,
    type: async (selector: string, value: string) => {
      const input = root.querySelector<HTMLInputElement>(selector)!;
      input.value = value;
      input.dispatchEvent(new Event('input'));
      await stable();
    },
    submit: async () => {
      root.querySelector('form:not([role="search"])')!.dispatchEvent(new Event('submit', { cancelable: true }));
      await stable();
      await new Promise((resolve) => setTimeout(resolve, 10));
      await stable();
    },
  };
}

describe('SalePage: held sales', () => {
  it('puts the cart on hold with a label and lists it with its count and total', async () => {
    const { cash } = await setup();
    cash.add(jus, 2);
    const fixture = TestBed.createComponent(SalePage);
    await fixture.whenStable();
    const root = fixture.nativeElement as HTMLElement;
    const ui = helpers(root, () => fixture.whenStable());

    await ui.click('Mettre en attente');
    await ui.type('#attente-nom', 'Mme Koné');
    await ui.submit();

    // La mise en attente passe par IndexedDB : sous charge, l'écran se stabilise avant la fin de l'écriture, on attend donc le résultat.
    await vi.waitFor(() => expect(root.textContent).toContain('Ventes en attente (1)'));
    expect(cash.cart()).toEqual([]);
    expect(root.textContent).toContain('Mme Koné');
    expect(root.textContent).toContain('2 articles');
    expect(root.textContent).toContain('1\u00a0200'); // espace insécable des milliers
    expect(root.querySelector('[role="status"]')?.textContent).toContain('Vente mise en attente');
  });

  it('resumes a held sale into the empty cart', async () => {
    const { cash } = await setup();
    cash.add(jus, 3);
    await cash.holdCurrent('A');
    const fixture = TestBed.createComponent(SalePage);
    await fixture.whenStable();
    const ui = helpers(fixture.nativeElement, () => fixture.whenStable());

    await ui.click('Reprendre');

    // La reprise passe par IndexedDB : sous charge, l'écran se stabilise avant la fin de l'écriture, on attend donc le résultat.
    await vi.waitFor(() => expect(cash.held()).toEqual([]));
    expect(cash.cart()).toEqual([{ productId: 'p-jus', name: 'Jus', unitPriceFcfa: 600, quantity: 3 }]);
  });

  it('explains why a held sale cannot be resumed over a non empty cart, and keeps everything', async () => {
    const { cash } = await setup();
    cash.add(jus);
    await cash.holdCurrent('A');
    cash.add(jus, 5);
    const fixture = TestBed.createComponent(SalePage);
    await fixture.whenStable();
    const root = fixture.nativeElement as HTMLElement;
    const ui = helpers(root, () => fixture.whenStable());

    await ui.click('Reprendre');

    expect(root.querySelector('[role="alert"]')?.textContent).toContain('panier en cours');
    expect(cash.cart()[0].quantity).toBe(5);
    expect(cash.held()).toHaveLength(1);
  });

  it('asks for a confirmation before deleting a held sale, and lets the user keep it', async () => {
    const { cash } = await setup();
    cash.add(jus);
    await cash.holdCurrent('A');
    const fixture = TestBed.createComponent(SalePage);
    await fixture.whenStable();
    const root = fixture.nativeElement as HTMLElement;
    const ui = helpers(root, () => fixture.whenStable());

    await ui.click('Supprimer la vente en attente');
    expect(root.textContent).toContain('Supprimer cette vente en attente ?');
    expect(cash.held()).toHaveLength(1);

    await ui.click('Non, la garder');
    await vi.waitFor(() => expect(root.textContent).not.toContain('Supprimer cette vente en attente ?'));
    expect(cash.held()).toHaveLength(1);

    await ui.click('Supprimer la vente en attente');
    await ui.click('Oui, supprimer');
    await vi.waitFor(() => expect(cash.held()).toEqual([]));
  });
});

describe('ClosePage: held sales warning', () => {
  it('warns about the held sales that will be dropped at closing', async () => {
    const { cash } = await setup();
    cash.add(jus);
    await cash.holdCurrent('A');
    cash.add(jus, 2);
    await cash.holdCurrent('B');
    const fixture = TestBed.createComponent(ClosePage);
    await fixture.whenStable();

    const note = (fixture.nativeElement as HTMLElement).querySelector('[role="note"]');
    expect(note?.textContent).toContain('2 ventes en attente');
    expect(note?.textContent).toContain('supprimées');
  });

  it('shows no warning when nothing is on hold', async () => {
    await setup();
    const fixture = TestBed.createComponent(ClosePage);
    await fixture.whenStable();
    expect((fixture.nativeElement as HTMLElement).querySelector('[role="note"]')).toBeNull();
  });
});

const water = (id: string, label: string, price: number, quantityOnHand: number): CatalogueProduct => ({
  id, name: `Eau minérale · ${label}`, barcode: null, category: null, salePriceFcfa: price, tracksStock: true, quantityOnHand,
  familyId: 'eau', familyName: 'Eau minérale', formatLabel: label, photoId: null,
});
const rice = (id: string, label: string, price: number): CatalogueProduct => ({
  id, name: `Riz parfumé · ${label}`, barcode: null, category: null, salePriceFcfa: price, tracksStock: true, quantityOnHand: 20,
  familyId: 'riz', familyName: 'Riz parfumé', formatLabel: label, photoId: null,
});
const catalogue = [
  water('w15', '1,5 L', 400, 12), water('w1', '1 L', 300, 3), water('w30', '30 cl', 150, 0),
  rice('r5', '5 kg', 3200), rice('r25', '25 kg', 14500), rice('r50', '50 kg', 27000),
  { id: 'sel', name: 'Sel fin', barcode: '999', category: null, salePriceFcfa: 200, tracksStock: true, quantityOnHand: 40 },
];

async function openSale() {
  const { cash } = await setup({ products: vi.fn().mockResolvedValue(catalogue) }, catalogue);
  const fixture = TestBed.createComponent(SalePage);
  await fixture.whenStable();
  const root = fixture.nativeElement as HTMLElement;
  const ui = helpers(root, () => fixture.whenStable());
  await vi.waitFor(() => {
    fixture.detectChanges();
    expect(root.textContent).toContain('Eau minérale');
  });
  return { cash, fixture, root, ui };
}

describe('SalePage: families and formats', () => {
  it('shows one card per family, not one per format', async () => {
    const { root } = await openSale();

    const cards = [...root.querySelectorAll('ul.grid > li')].map((card) => card.textContent ?? '');
    expect(cards).toHaveLength(3);
    expect(cards.filter((card) => card.includes('Eau minérale'))).toHaveLength(1);
    expect(root.textContent).toContain('3 formats');
    expect(root.textContent).not.toContain('Eau minérale · 1 L');
  });

  it('adds a single-format product with one touch, as before', async () => {
    const { cash, ui } = await openSale();

    await ui.click('Sel fin');

    expect(cash.cart().map((line) => line.name)).toEqual(['Sel fin']);
  });

  it('opens the choice of format with the price and the stock of each, and adds the chosen one', async () => {
    const { cash, root, ui } = await openSale();

    await ui.click('Eau minérale');
    const choice = root.querySelector('[role="group"]')!;
    expect(choice.textContent).toContain('1,5 L');
    expect(choice.textContent).toContain('1 L');
    expect(choice.textContent).toContain('30 cl');
    expect(choice.textContent).toContain('Plus que 3');
    expect(choice.textContent).toContain('Rupture de stock');
    expect(cash.cart()).toEqual([]);

    await ui.click('Eau minérale · 1 L');

    expect(cash.cart().map((line) => [line.name, line.unitPriceFcfa])).toEqual([['Eau minérale · 1 L', 300]]);
    expect(root.querySelector('[role="group"]')).toBeNull();
  });

  it('lists formats from the cheapest to the dearest', async () => {
    const { root, ui } = await openSale();

    await ui.click('Riz parfumé');

    const labels = [...root.querySelectorAll('[role="group"] button')].map((button) => button.textContent ?? '');
    expect(labels[0]).toContain('5 kg');
    expect(labels[1]).toContain('25 kg');
    expect(labels[2]).toContain('50 kg');
  });

  it('finds a format by its label and adds it with one touch when only that one is left', async () => {
    const { cash, ui } = await openSale();

    await ui.type('#recherche', 'riz 25');
    await vi.waitFor(async () => {
      await ui.click('Riz parfumé · 25 kg');
      expect(cash.cart().map((line) => line.name)).toEqual(['Riz parfumé · 25 kg']);
    });
  });

  it('keeps two formats of the same family on two lines of the cart', async () => {
    const { cash, ui } = await openSale();

    await ui.click('Eau minérale');
    await ui.click('Eau minérale · 1,5 L');
    await ui.click('Eau minérale');
    await ui.click('Eau minérale · 1 L');

    expect(cash.cart().map((line) => line.name).sort()).toEqual(['Eau minérale · 1 L', 'Eau minérale · 1,5 L']);
  });

  it('closes the choice when the family card is touched again', async () => {
    const { root, ui } = await openSale();

    await ui.click('Eau minérale');
    expect(root.querySelector('[role="group"]')).not.toBeNull();
    await ui.click('Eau minérale');

    expect(root.querySelector('[role="group"]')).toBeNull();
  });

  it('adds the exact format of a scanned barcode', async () => {
    const withCodes = catalogue.map((product) => (product.id === 'w1' ? { ...product, barcode: '222' } : product));
    const { cash } = await setup({ products: vi.fn().mockResolvedValue(withCodes) }, withCodes);
    const fixture = TestBed.createComponent(SalePage);
    await fixture.whenStable();
    const root = fixture.nativeElement as HTMLElement;
    const ui = helpers(root, () => fixture.whenStable());
    await vi.waitFor(() => {
      fixture.detectChanges();
      expect(root.textContent).toContain('Eau minérale');
    });

    await ui.type('#recherche', '222');
    root.querySelector('form[role="search"]')!.dispatchEvent(new Event('submit', { cancelable: true }));
    await vi.waitFor(() => expect(cash.cart().map((line) => line.name)).toEqual(['Eau minérale · 1 L']));

  });
});

describe('SalePage: catalogue', () => {
  it('shows a product created on the server after the till was signed in, without signing in again', async () => {
    const baguette: CatalogueProduct = {
      id: 'p-baguette', name: 'Baguette', barcode: null, category: null, salePriceFcfa: 150, tracksStock: false, quantityOnHand: 0,
    };
    await setup({ products: vi.fn().mockResolvedValue([jus, baguette]) });
    const fixture = TestBed.createComponent(SalePage);
    await fixture.whenStable();

    await vi.waitFor(() => {
      fixture.detectChanges();
      expect((fixture.nativeElement as HTMLElement).textContent).toContain('Baguette');
    });
  });
});

describe('SalePage: abonnement terminé', () => {
  async function open(role: string, readOnly: boolean) {
    const { cash } = await setup({}, [jus], role, signal(readOnly));
    cash.add(jus, 1);
    const fixture = TestBed.createComponent(SalePage);
    await fixture.whenStable();
    const root = fixture.nativeElement as HTMLElement;
    const ui = helpers(root, () => fixture.whenStable());
    return { root, ui, fixture };
  }

  const pay = (root: HTMLElement) => [...root.querySelectorAll('button')].filter((button) => (button.textContent ?? '').trim() === 'Encaisser');

  it('lets the till charge normally when the subscription is fine', async () => {
    const { root, ui } = await open('Cashier', false);

    expect(pay(root).every((button) => !button.disabled)).toBe(true);
    expect(root.textContent).not.toContain('Vente suspendue');
    await ui.click('Encaisser');
    expect(root.querySelector('app-payment-panel')).not.toBeNull();
  });

  it('turns « Encaisser » off for a cashier and tells them to warn their manager, without any price', async () => {
    const { root, fixture } = await open('Cashier', true);

    expect(pay(root).length).toBeGreaterThan(0);
    expect(pay(root).every((button) => button.disabled)).toBe(true);
    expect(root.textContent).toContain('Vente suspendue. Prévenez votre responsable');
    expect(root.querySelector('a[href="/gestion/abonnement"]')).toBeNull();
    pay(root)[0].click();
    await fixture.whenStable();
    expect(root.querySelector('app-payment-panel')).toBeNull();
  });

  it('gives the owner the reason and a link to the subscription page', async () => {
    const { root } = await open('Owner', true);

    expect(pay(root).every((button) => button.disabled)).toBe(true);
    expect(root.textContent).toContain('La vente est suspendue : l’abonnement est terminé.');
    expect(root.querySelector('a[href="/gestion/abonnement"]')?.textContent).toContain('Voir mon abonnement');
  });
});

describe('SalePage: barcode scanner (ADR 0015)', () => {
  const eau: CatalogueProduct = {
    id: 'p-eau', name: 'Eau minérale 1,5 L', barcode: '6111000000011', category: null, salePriceFcfa: 500, tracksStock: true, quantityOnHand: 40,
  };

  /** Les touches d'un code, comme un lecteur les envoie, sur la cible donnée. */
  function scan(target: EventTarget, code: string, ending: 'Enter' | 'Tab' | null = 'Enter'): void {
    typeScan(target, code, { ending });
  }

  /** Attend qu'une vérification passe (le scan est asynchrone : IndexedDB). */
  async function until(check: () => void, options: { timeout?: number } = {}): Promise<void> {
    const end = Date.now() + (options.timeout ?? 2000);
    for (;;) {
      try {
        check();
        return;
      } catch (error) {
        if (Date.now() > end) {
          throw error;
        }
        await new Promise((resolve) => setTimeout(resolve, 25));
      }
    }
  }

  async function open() {
    const { cash } = await setup({}, [eau, jus]);
    const fixture = TestBed.createComponent(SalePage);
    await fixture.whenStable();
    const root = fixture.nativeElement as HTMLElement;
    await vi.waitFor(() => {
      fixture.detectChanges();
      expect(root.textContent).toContain('Eau minérale');
    });
    return { cash, fixture, root, field: root.querySelector<HTMLInputElement>('#recherche')! };
  }

  it('adds the product when a scan arrives with nothing focused, and says so', async () => {
    const { cash, fixture, root } = await open();

    scan(document.body, eau.barcode!);
    await until(() => expect(cash.cart().map((line) => line.name)).toEqual(['Eau minérale 1,5 L']));
    await fixture.whenStable();
    expect(root.querySelector('[role="status"]')?.textContent).toContain('Eau minérale 1,5 L ajouté au panier');
  });

  it('adds the product when the scan has no ending, after the silence', async () => {
    const { cash } = await open();

    scan(document.body, eau.barcode!, null);

    await until(() => expect(cash.cart()).toHaveLength(1), { timeout: 1000 });
  });

  it('adds the product from the search field with Enter, and leaves the field empty', async () => {
    const { cash, field } = await open();
    field.focus();

    scan(field, eau.barcode!);

    await until(() => expect(cash.cart()).toHaveLength(1));
    await until(() => expect(field.value).toBe(''));
  });

  it('counts two scans of the same product as a quantity of 2', async () => {
    const { cash } = await open();

    scan(document.body, eau.barcode!);
    await until(() => expect(cash.cart()).toHaveLength(1));
    await new Promise((resolve) => setTimeout(resolve, 60));
    scan(document.body, eau.barcode!);

    await until(() => expect(cash.cart()[0].quantity).toBe(2));
  });

  it('says the code is unknown, with the code, and adds nothing', async () => {
    const { cash, fixture, root } = await open();

    scan(document.body, '9999999999999');

    await until(() => {
      fixture.detectChanges();
      expect(root.querySelector('.field-error')?.textContent).toContain('Aucun produit pour le code 9999999999999.');
    });
    expect(cash.cart()).toEqual([]);
  });

  it('treats the text of a QR code as an unknown code, shortened, and opens nothing', async () => {
    const { cash, fixture, root } = await open();
    const text = 'https://exemple.ci/promo/' + '1'.repeat(60);

    // Un QR code est du texte : on le « tape » comme un code (les lettres comptent comme des caractères ordinaires).
    typeScan(document.body, text);

    await until(() => {
      fixture.detectChanges();
      expect(root.querySelector('.field-error')?.textContent).toContain('Aucun produit pour le code');
    });
    expect(root.querySelector('.field-error')?.textContent?.length).toBeLessThan(80);
    expect(cash.cart()).toEqual([]);
  });

  it('does not steal a scan typed into another field', async () => {
    const { cash } = await open();
    const other = document.createElement('input');
    document.body.append(other);
    other.focus();

    scan(other, eau.barcode!);
    await new Promise((resolve) => setTimeout(resolve, 250));
    other.remove();

    expect(cash.cart()).toEqual([]);
  });

  it('keeps the search by name for a person typing', async () => {
    const { cash, fixture, root, field } = await open();
    field.focus();

    for (const character of 'Eau') {
      field.dispatchEvent(new KeyboardEvent('keydown', { key: character, code: `Key${character.toUpperCase()}`, bubbles: true }));
      await new Promise((resolve) => setTimeout(resolve, 70));
    }
    field.value = 'Eau';
    field.dispatchEvent(new Event('input'));
    await fixture.whenStable();

    await until(() => {
      fixture.detectChanges();
      expect(root.textContent).not.toContain('Jus');
    });
    expect(root.textContent).toContain('Eau minérale');
    expect(cash.cart()).toEqual([]);
  });

  it('shows a visible message when a scan adds a product, and again for the same product scanned twice', async () => {
    const { fixture, root } = await open();

    scan(document.body, eau.barcode!);
    await until(() => {
      fixture.detectChanges();
      expect(root.querySelector('[data-testid="scan-note"]')?.textContent).toContain('Eau minérale 1,5 L ajouté au panier');
    });

    const first = root.querySelector('[data-testid="scan-note"]');
    await new Promise((resolve) => setTimeout(resolve, 60));
    scan(document.body, eau.barcode!);
    await until(() => {
      fixture.detectChanges();
      const second = root.querySelector('[data-testid="scan-note"]');
      expect(second?.textContent).toContain('ajouté au panier');
      expect(second).not.toBe(first); // un nouvel élément : un lecteur d'écran l'annonce de nouveau
    });
  });

  it('starts the next sale when the next customer\'s first item is scanned on the ticket screen', async () => {
    const { cash, fixture } = await open();
    cash.add(jus);
    fixture.componentInstance['paying'].set(true);
    fixture.detectChanges();
    const panel = fixture.debugElement.query(By.directive(PaymentPanel)).componentInstance as PaymentPanel;
    panel['confirmed'].set({ kind: 'sale', netTotalFcfa: 600, changeFcfa: 0, payments: [], lines: [] } as unknown as SaleOperation);
    cash.remove(jus.id); // la vente est encaissée : le panier est vide, c'est l'écran du ticket qui reste
    fixture.detectChanges();

    scan(document.body, eau.barcode!);

    await until(() => expect(cash.cart().map((line) => line.name)).toEqual(['Eau minérale 1,5 L']));
    expect(fixture.componentInstance['paying']()).toBe(false);
  });

  it('tells the cashier to finish the payment instead of ignoring a scan in silence', async () => {
    const { cash, fixture, root } = await open();
    cash.add(eau);
    fixture.componentInstance['paying'].set(true);
    fixture.detectChanges();

    scan(document.body, '6111000000028');
    await new Promise((resolve) => setTimeout(resolve, 200));
    fixture.detectChanges();

    expect(cash.cart()).toHaveLength(1);
    expect(root.textContent).toContain('Terminez ou annulez le paiement');
  });
});

describe('SalePage: last ticket (docs/specs/etiquettes-tickets TKT-01)', () => {
  const lastSale = {
    kind: 'sale', seq: 5, status: 'pending', saleId: 's', sessionId: 'sess', at: new Date().toISOString(), discountFcfa: 0, netTotalFcfa: 600, changeFcfa: 0,
    lines: [{ productId: 'p-jus', name: 'Jus', unitPriceFcfa: 600, quantity: 1 }], payments: [{ id: 'p', method: 'Cash', amountFcfa: 600, tenderedFcfa: 600 }],
  } as SaleOperation;

  async function open(withReceipt: boolean) {
    await setup();
    if (withReceipt) {
      await TestBed.inject(ReceiptService).remember(lastSale, null);
    }
    const fixture = TestBed.createComponent(SalePage);
    await fixture.whenStable();
    const root = fixture.nativeElement as HTMLElement;
    const click = async (label: string) => {
      [...root.querySelectorAll('button')].find((button) => button.textContent?.includes(label))!.click();
      await fixture.whenStable();
      fixture.detectChanges();
    };
    return { root, click, has: (text: string) => (root.textContent ?? '').includes(text) };
  }

  it('offers nothing before a first sale', async () => {
    expect((await open(false)).has('Réimprimer le dernier ticket')).toBe(false);
  });

  it('offers to print the last ticket again, with the same ways out as at the payment, then closes', async () => {
    const page = await open(true);
    expect(page.has('Imprimer le ticket')).toBe(false);

    await page.click('Réimprimer le dernier ticket');
    expect(page.has('Imprimer le ticket')).toBe(true);
    expect(page.has('Copier le ticket')).toBe(true);

    await page.click('Fermer');
    expect(page.has('Imprimer le ticket')).toBe(false);
  });
});

describe('SalePage: weighed products (docs/specs/vente-au-poids)', () => {
  const riz: CatalogueProduct = {
    id: 'p-riz', name: 'Riz parfumé', barcode: '6111000000028', category: null, salePriceFcfa: 800, tracksStock: true, quantityOnHand: 25_000, unit: 'Kilogram',
  };

  async function open() {
    const { cash } = await setup({}, [riz, jus]);
    const fixture = TestBed.createComponent(SalePage);
    await fixture.whenStable();
    const root = fixture.nativeElement as HTMLElement;
    await vi.waitFor(() => {
      fixture.detectChanges();
      expect(root.textContent).toContain('Riz parfumé');
    });
    const settle = async () => {
      await fixture.whenStable();
      await new Promise((resolve) => setTimeout(resolve, 20));
      fixture.detectChanges();
    };
    const click = async (label: string) => {
      const target = [...root.querySelectorAll('button')].find((button) => (button.textContent ?? '').includes(label) || button.getAttribute('aria-label')?.includes(label));
      expect(target, `bouton « ${label} » introuvable`).toBeTruthy();
      target!.click();
      await settle();
    };
    const type = async (value: string) => {
      const field = root.querySelector<HTMLInputElement>('#peser-valeur')!;
      field.value = value;
      field.dispatchEvent(new Event('input'));
      await settle();
    };
    return { cash, fixture, root, click, type, settle, has: (text: string) => (root.textContent ?? '').includes(text) };
  }

  it('shows the price of one kilo and the stock in kilos on the product', async () => {
    const page = await open();

    expect(page.root.textContent).toContain('800\u00a0FCFA / kg');
    expect(page.root.textContent).toContain('Stock : 25,000 kg');
  });

  it('opens the weighing instead of adding one when a weighed product is touched', async () => {
    const page = await open();

    await page.click('Riz parfumé');

    expect(page.has('Peser : Riz parfumé')).toBe(true);
    expect(page.cash.cart()).toEqual([]);
  });

  it('adds the weighed quantity in grams with its unit, shows it in words and totals it', async () => {
    const page = await open();
    await page.click('Riz parfumé');

    await page.type('1,25');
    await page.click('Ajouter au panier');

    expect(page.cash.cart()).toEqual([{ productId: 'p-riz', name: 'Riz parfumé', unitPriceFcfa: 800, quantity: 1250, unit: 'Kilogram' }]);
    expect(page.has('1,250 kg')).toBe(true);
    expect(page.cash.total()).toBe(1000);
    expect(page.has('Peser : Riz parfumé')).toBe(false);
    expect(page.root.querySelector('[data-testid="scan-note"]')?.textContent).toContain('Riz parfumé ajouté au panier');
  });

  it('adds « pour 1 000 francs » as 1,250 kg', async () => {
    const page = await open();
    await page.click('Riz parfumé');
    await page.click('Montant');

    await page.type('1000');
    await page.click('Ajouter au panier');

    expect(page.cash.cart()[0].quantity).toBe(1250);
  });

  it('corrects the weight of a line by retyping it, without plus and minus buttons', async () => {
    const page = await open();
    await page.click('Riz parfumé');
    await page.type('1,25');
    await page.click('Ajouter au panier');
    expect(page.root.querySelector('[aria-label="Augmenter la quantité de Riz parfumé"]')).toBeNull();

    await page.click('Modifier le poids de Riz parfumé');
    expect(page.root.querySelector<HTMLInputElement>('#peser-valeur')!.value).toBe('1,250');
    await page.type('2');
    await page.click('Enregistrer');

    expect(page.cash.cart()[0].quantity).toBe(2000);
    expect(page.cash.total()).toBe(1600);
  });

  it('counts a weighed line as one article, not as thousands of them', async () => {
    const page = await open();
    page.cash.add(riz, 1250);
    page.cash.add(jus, 2);
    page.fixture.detectChanges();

    expect(page.has('3 articles')).toBe(true);
    expect(page.has('1 250 articles')).toBe(false);
  });

  it('remembers what is already in the cart when it checks the stock', async () => {
    const page = await open();
    page.cash.add(riz, 20_000);
    page.fixture.detectChanges();
    await page.click('Riz parfumé');

    await page.type('6');

    expect(page.root.querySelector('[role="alert"]')?.textContent).toContain('Il n’en reste que 5,000 kg.');
  });

  it('opens the weighing when a weighed product is scanned, and leaves the pieces to the scan as before', async () => {
    const page = await open();
    const typeScan = (await import('../core/scan-test-support')).typeScan;

    typeScan(document.body, riz.barcode!);
    await vi.waitFor(() => {
      page.fixture.detectChanges();
      expect(page.has('Peser : Riz parfumé')).toBe(true);
    });
    expect(page.cash.cart()).toEqual([]);
  });
});
