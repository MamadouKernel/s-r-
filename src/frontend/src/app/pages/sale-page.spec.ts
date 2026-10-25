import 'fake-indexeddb/auto';
import { TestBed } from '@angular/core/testing';
import { Journal } from '../data/journal';
import { CatalogueProduct } from '../domain/types';
import { ApiService } from '../services/api.service';
import { AuthService } from '../services/auth.service';
import { CashService } from '../services/cash.service';
import { CatalogueService } from '../services/catalogue.service';
import { JournalService } from '../services/journal.service';
import { SyncService } from '../services/sync.service';
import { ClosePage } from './close-page';
import { SalePage } from './sale-page';

const jus: CatalogueProduct = {
  id: 'p-jus', name: 'Jus', barcode: '111', category: null, salePriceFcfa: 600, tracksStock: true, quantityOnHand: 24,
};

async function setup(api: Partial<ApiService> = {}) {
  const journal = await Journal.open(`soro-ui-${Math.random()}`);
  await journal.replaceCatalogue([jus]);
  TestBed.configureTestingModule({
    providers: [
      { provide: JournalService, useValue: { get: () => Promise.resolve(journal) } },
      { provide: SyncService, useValue: { requestFlush: vi.fn().mockResolvedValue(null), counts: () => ({ pending: 0, synced: 0, rejected: 0 }) } },
      { provide: ApiService, useValue: api },
      { provide: AuthService, useValue: { device: () => ({ tenantId: 't', registerId: 'r', pointOfSaleId: 'p' }) } },
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
