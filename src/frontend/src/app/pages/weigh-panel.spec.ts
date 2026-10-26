import { TestBed } from '@angular/core/testing';
import { CatalogueProduct } from '../domain/types';
import { WeighPanel } from './weigh-panel';

const riz: CatalogueProduct = {
  id: 'p-riz', name: 'Riz parfumé', barcode: null, category: null, salePriceFcfa: 800, tracksStock: true, quantityOnHand: 25_000, unit: 'Kilogram',
};
const huile: CatalogueProduct = { ...riz, id: 'p-huile', name: 'Huile', salePriceFcfa: 1500, unit: 'Litre' };

async function open(product: CatalogueProduct = riz, options: { available?: number | null; current?: number } = {}) {
  TestBed.resetTestingModule();
  const fixture = TestBed.createComponent(WeighPanel);
  fixture.componentRef.setInput('product', product);
  fixture.componentRef.setInput('available', options.available === undefined ? product.quantityOnHand : options.available);
  if (options.current !== undefined) {
    fixture.componentRef.setInput('current', options.current);
  }
  const confirmed: number[] = [];
  let cancelled = 0;
  fixture.componentInstance.confirmed.subscribe((quantity) => confirmed.push(quantity));
  fixture.componentInstance.cancelled.subscribe(() => cancelled++);
  await fixture.whenStable();
  const root = fixture.nativeElement as HTMLElement;
  const settle = async () => {
    await fixture.whenStable();
    fixture.detectChanges();
  };
  return {
    root,
    confirmed,
    cancelled: () => cancelled,
    has: (text: string) => (root.textContent ?? '').includes(text),
    type: async (value: string) => {
      const field = root.querySelector<HTMLInputElement>('#peser-valeur')!;
      field.value = value;
      field.dispatchEvent(new Event('input'));
      await settle();
    },
    click: async (label: string) => {
      const button = [...root.querySelectorAll('button')].find((candidate) => (candidate.textContent ?? '').includes(label));
      expect(button, `bouton « ${label} » introuvable`).toBeTruthy();
      button!.click();
      await settle();
    },
    add: () => [...root.querySelectorAll('button')].find((candidate) => (candidate.textContent ?? '').includes('Ajouter au panier'))!,
  };
}

describe('WeighPanel', () => {
  it('names the product with the price of one kilo and asks for the weight', async () => {
    const panel = await open();

    expect(panel.has('Riz parfumé')).toBe(true);
    expect(panel.has('800\u00a0FCFA / kg')).toBe(true);
    expect(panel.root.querySelector('label[for="peser-valeur"]')?.textContent).toContain('Poids en kg');
  });

  it('shows the weight and the total before adding, then adds the exact quantity in grams', async () => {
    const panel = await open();

    await panel.type('1,25');
    expect(panel.has('1,250 kg · 1 000 FCFA')).toBe(true);
    await panel.click('Ajouter au panier');

    expect(panel.confirmed).toEqual([1250]);
  });

  it('computes the weight from an amount: « pour 1 000 francs de riz »', async () => {
    const panel = await open();
    await panel.click('Montant');

    expect(panel.root.querySelector('label[for="peser-valeur"]')?.textContent).toContain('Montant en FCFA');
    await panel.type('1000');
    expect(panel.has('1,250 kg · 1 000 FCFA')).toBe(true);
    await panel.click('Ajouter au panier');

    expect(panel.confirmed).toEqual([1250]);
  });

  it('says when the amount does not come out round: the weight is rounded to the gram and the real total is shown', async () => {
    const panel = await open({ ...riz, salePriceFcfa: 700 });
    await panel.click('Montant');

    await panel.type('1000');

    expect(panel.has('1,429 kg · 1 000 FCFA')).toBe(true);
  });

  it('does not scold before anything is typed: it gives an example and keeps the add button off', async () => {
    const panel = await open();

    expect(panel.root.querySelector('[role="alert"]')).toBeNull();
    expect(panel.has('Tapez le poids, par exemple 1,5.')).toBe(true);
    expect(panel.add().disabled).toBe(true);
  });

  it.each([
    ['abc', 'Écrivez un poids, par exemple 1,5.'],
    ['0', 'Le poids doit être supérieur à 0.'],
    ['0,0625', 'Écrivez le poids avec trois décimales au plus.'],
  ])('refuses %j with a sentence and keeps the add button off', async (value, message) => {
    const panel = await open();

    await panel.type(value);

    expect(panel.root.querySelector('[role="alert"]')?.textContent).toContain(message);
    expect(panel.add().disabled).toBe(true);
  });

  it('refuses more than what is left, with the stock in words', async () => {
    const panel = await open(riz, { available: 500 });

    await panel.type('1,25');

    expect(panel.root.querySelector('[role="alert"]')?.textContent).toContain('Il n’en reste que 0,500 kg.');
    expect(panel.add().disabled).toBe(true);
  });

  it('says when nothing is left', async () => {
    const panel = await open(riz, { available: 0 });

    await panel.type('1');

    expect(panel.root.querySelector('[role="alert"]')?.textContent).toContain('Il n’en reste plus.');
  });

  it('does not limit a product whose stock is not tracked', async () => {
    const panel = await open({ ...riz, tracksStock: false }, { available: null });

    await panel.type('40');

    expect(panel.add().disabled).toBe(false);
  });

  it('speaks of litres for a measured product', async () => {
    const panel = await open(huile);

    expect(panel.has('1 500 FCFA / L')).toBe(true);
    expect(panel.root.querySelector('label[for="peser-valeur"]')?.textContent).toContain('Quantité en litres');
    await panel.type('2');
    expect(panel.has('2,000 L · 3 000 FCFA')).toBe(true);
  });

  it('starts from the current weight when it corrects a line of the cart', async () => {
    const panel = await open(riz, { current: 1250 });

    expect(panel.root.querySelector<HTMLInputElement>('#peser-valeur')!.value).toBe('1,250');
    expect(panel.has('Modifier le poids')).toBe(true);
  });

  it('cancels without adding anything', async () => {
    const panel = await open();

    await panel.click('Annuler');

    expect(panel.cancelled()).toBe(1);
    expect(panel.confirmed).toEqual([]);
  });
});
