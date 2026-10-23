import { signal, Type } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter, Router } from '@angular/router';
import { AuthService } from '../services/auth.service';
import { FlashService } from './flash.service';
import { NetworkService } from '../services/network.service';
import { ManagedProduct, ProductsApi } from './products-api';

export const product = (overrides: Partial<ManagedProduct> = {}): ManagedProduct => ({
  id: 'p-1', name: "Jus d'orange 1L", barcode: '111', category: 'Boissons', purchasePriceFcfa: 400, salePriceFcfa: 600,
  alertThreshold: 5, tracksStock: true, isActive: true, quantityOnHand: 24, isLowStock: false, ...overrides,
});

/** Faux service de gestion : chaque méthode est un espion que le test configure. */
export function fakeApi() {
  return {
    list: vi.fn().mockResolvedValue([]),
    lowStock: vi.fn().mockResolvedValue([]),
    create: vi.fn().mockResolvedValue(undefined),
    update: vi.fn().mockResolvedValue(undefined),
    deactivate: vi.fn().mockResolvedValue(undefined),
    receive: vi.fn().mockResolvedValue({ movementId: 'm', quantityOnHand: 0, alreadyProcessed: false }),
    recordLoss: vi.fn().mockResolvedValue({ movementId: 'm', quantityOnHand: 0, alreadyProcessed: false }),
    movements: vi.fn().mockResolvedValue([]),
  };
}

export async function render<T>(
  component: Type<T>,
  options: { api: ReturnType<typeof fakeApi>; role?: string; params?: Record<string, string>; online?: boolean; flash?: string },
) {
  const flash = new FlashService();
  if (options.flash) {
    flash.show(options.flash);
  }
  TestBed.configureTestingModule({
    providers: [
      provideRouter([]),
      { provide: FlashService, useValue: flash },
      { provide: ProductsApi, useValue: options.api },
      { provide: AuthService, useValue: { user: signal({ id: 'u', displayName: 'Moussa', role: options.role ?? 'Manager' }) } },
      { provide: NetworkService, useValue: { online: signal(options.online ?? true) } },
      { provide: ActivatedRoute, useValue: { snapshot: { paramMap: convertToParamMap(options.params ?? {}) } } },
    ],
  });
  const navigate = vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);
  const fixture: ComponentFixture<T> = TestBed.createComponent(component);
  const root = fixture.nativeElement as HTMLElement;

  // Plusieurs chargements s'enchaînent à l'ouverture d'une page : on laisse le temps de tous les terminer.
  const settle = async () => {
    await fixture.whenStable();
    await new Promise((resolve) => setTimeout(resolve, 10));
    await fixture.whenStable();
    await new Promise((resolve) => setTimeout(resolve, 10));
    await fixture.whenStable();
  };
  await settle();
  return {
    fixture,
    flash,
    root,
    navigate,
    settle,
    type: async (selector: string, value: string) => {
      const input = root.querySelector<HTMLInputElement>(selector);
      expect(input, `champ ${selector} introuvable`).toBeTruthy();
      input!.value = value;
      input!.dispatchEvent(new Event('input'));
      await fixture.whenStable();
    },
    check: async (selector: string, checked: boolean) => {
      const input = root.querySelector<HTMLInputElement>(selector)!;
      input.checked = checked;
      input.dispatchEvent(new Event('change'));
      await settle();
    },
    click: async (label: string) => {
      const target = [...root.querySelectorAll('button, a')].find(
        (candidate) => (candidate.textContent ?? '').includes(label) || candidate.getAttribute('aria-label')?.includes(label),
      ) as HTMLElement | undefined;
      expect(target, `bouton « ${label} » introuvable`).toBeTruthy();
      target!.click();
      await settle();
    },
    submit: async (formIndex = 0) => {
      root.querySelectorAll('form')[formIndex].dispatchEvent(new Event('submit', { cancelable: true }));
      await settle();
    },
    has: (text: string) => (root.textContent ?? '').includes(text),
  };
}
