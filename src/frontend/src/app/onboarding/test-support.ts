import { Provider, signal, Type } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, provideRouter, Router } from '@angular/router';
import { FlashService } from '../management/flash.service';
import { AuthService } from '../services/auth.service';
import { CustomerService } from '../customers/customer.service';
import { CashService } from '../services/cash.service';
import { CatalogueService } from '../services/catalogue.service';
import { SyncService } from '../services/sync.service';
import { NetworkService } from '../services/network.service';
import { OnboardingApi, OwnerSession } from './onboarding-api';

export const ownerSession = (overrides: Partial<OwnerSession> = {}): OwnerSession => ({
  accessToken: 'jwt', tokenType: 'Bearer', expiresAt: '2026-10-25T08:00:00.000Z',
  user: { id: 'o', displayName: 'Mamadou', role: 'Owner' }, tenantId: 't-1', commerceName: 'Ma Boutique', trialEndsAt: '2026-11-23T08:00:00.000Z', ...overrides,
});

export function fakeOnboardingApi() {
  return {
    startSignup: vi.fn().mockResolvedValue({ verificationId: 'v-1', expiresAt: '2099-01-01T00:10:00.000Z', resendAvailableAt: '2000-01-01T00:00:00.000Z' }),
    resendCode: vi.fn().mockResolvedValue({ verificationId: 'v-1', expiresAt: '2099-01-01T00:10:00.000Z', resendAvailableAt: '2000-01-01T00:00:00.000Z' }),
    completeSignup: vi.fn().mockResolvedValue(ownerSession()),
    ownerLogin: vi.fn().mockResolvedValue(ownerSession()),
    startPasswordReset: vi.fn().mockResolvedValue(undefined),
    confirmPasswordReset: vi.fn().mockResolvedValue(undefined),
    enrollDevice: vi.fn(),
  };
}

export function fakeAuth(overrides: Record<string, unknown> = {}) {
  return {
    acceptOwnerSession: vi.fn().mockResolvedValue(undefined),
    enrollDevice: vi.fn().mockResolvedValue({ tenantId: 't-1', commerceName: 'Ma Boutique', deviceId: 'd-1', name: 'Caisse' }),
    markDeviceRefused: vi.fn().mockResolvedValue(undefined),
    device: signal<object | null>(null),
    user: signal({ id: 'o', displayName: 'Mamadou', role: 'Owner' }),
    ...overrides,
  };
}

/** Rend une page d'inscription ou de connexion avec de faux services et des aides pour taper, cocher, cliquer. */
export async function renderPage<T>(
  component: Type<T>,
  options: { api?: ReturnType<typeof fakeOnboardingApi>; auth?: ReturnType<typeof fakeAuth>; online?: boolean; flash?: string; fragment?: string | null; extraProviders?: Provider[] } = {},
) {
  const flash = new FlashService();
  if (options.flash) {
    flash.show(options.flash);
  }
  const api = options.api ?? fakeOnboardingApi();
  const auth = options.auth ?? fakeAuth();
  TestBed.resetTestingModule();
  TestBed.configureTestingModule({
    providers: [
      provideRouter([]),
      { provide: FlashService, useValue: flash },
      { provide: OnboardingApi, useValue: api },
      { provide: AuthService, useValue: auth },
      { provide: CashService, useValue: { session: signal(null).asReadonly() } },
      { provide: CatalogueService, useValue: { refresh: vi.fn().mockResolvedValue(undefined) } },
      { provide: CustomerService, useValue: { refresh: vi.fn().mockResolvedValue(undefined) } },
      { provide: SyncService, useValue: { requestFlush: vi.fn().mockResolvedValue(null) } },
      { provide: NetworkService, useValue: { online: signal(options.online ?? true) } },
      { provide: ActivatedRoute, useValue: { snapshot: { fragment: options.fragment ?? null, paramMap: { get: () => null } } } },
      ...(options.extraProviders ?? []),
    ],
  });
  const navigate = vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);
  const fixture = TestBed.createComponent(component);
  const root = fixture.nativeElement as HTMLElement;
  const settle = async () => {
    await fixture.whenStable();
    await new Promise((resolve) => setTimeout(resolve, 5));
    await fixture.whenStable();
  };
  await settle();
  return {
    fixture, root, api, auth, flash, navigate, settle,
    type: async (selector: string, value: string) => {
      const input = root.querySelector<HTMLInputElement>(selector);
      expect(input, `champ ${selector} introuvable`).toBeTruthy();
      input!.value = value;
      input!.dispatchEvent(new Event('input'));
      await fixture.whenStable();
    },
    choose: async (selector: string, value: string) => {
      const select = root.querySelector<HTMLSelectElement>(selector)!;
      select.value = value;
      select.dispatchEvent(new Event('change'));
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
    alertText: () => [...root.querySelectorAll('[role="alert"]')].map((node) => node.textContent ?? '').join(' '),
    field: (selector: string) => root.querySelector<HTMLInputElement>(selector)!,
  };
}
