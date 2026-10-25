import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { AuthService } from '../services/auth.service';
import { SubscriptionBanner } from './subscription-banner';
import { SubscriptionView } from './subscription-api';
import { SubscriptionService } from './subscription.service';

const base: SubscriptionView = {
  status: 'Grace', endsAt: '2026-10-20T08:00:00Z', readOnlyFrom: '2026-10-27T08:00:00Z', daysLeft: 2, isReadOnly: false,
  monthlyPriceFcfa: 5000, annualPriceFcfa: 50000, payNumbers: null, payments: null,
};

async function render(view: SubscriptionView | null, role = 'Owner', signedIn = true) {
  TestBed.configureTestingModule({
    providers: [
      provideRouter([]),
      { provide: AuthService, useValue: { signedIn: signal(signedIn), user: signal({ id: 'u', displayName: 'Awa', role }) } },
      { provide: SubscriptionService, useValue: { view: signal(view) } },
    ],
  });
  const fixture = TestBed.createComponent(SubscriptionBanner);
  await fixture.whenStable();
  return fixture.nativeElement as HTMLElement;
}

describe('SubscriptionBanner', () => {
  it('shows nothing when the subscription is fine, unknown, or nobody is signed in', async () => {
    expect((await render({ ...base, status: 'Active', daysLeft: 30 })).querySelector('[role="status"]')).toBeNull();
    TestBed.resetTestingModule();
    expect((await render(null)).querySelector('[role="status"]')).toBeNull();
    TestBed.resetTestingModule();
    expect((await render(base, 'Owner', false)).querySelector('[role="status"]')).toBeNull();
  });

  it('shows the owner the message and a button to the subscription page', async () => {
    const root = await render(base);

    expect(root.querySelector('[role="status"]')?.textContent).toContain('Abonnement échu. Plus que 2 jours avant la lecture seule');
    const link = root.querySelector('a');
    expect(link?.getAttribute('href')).toBe('/gestion/abonnement');
    expect(link?.textContent).toContain('Payer maintenant');
  });

  it('shows a cashier a short message with no button and no price', async () => {
    const root = await render({ ...base, status: 'ReadOnly', isReadOnly: true, daysLeft: 0 }, 'Cashier');

    expect(root.querySelector('[role="status"]')?.textContent).toContain('lecture seule');
    expect(root.querySelector('a')).toBeNull();
    expect(root.textContent).not.toContain('FCFA');
  });

  it('is announced politely and the tone changes the look, not the meaning', async () => {
    const root = await render({ ...base, status: 'ReadOnly', isReadOnly: true, daysLeft: 0 });
    const banner = root.querySelector('[role="status"]')!;

    expect(banner.getAttribute('aria-live')).toBe('polite');
    expect(banner.className).toContain('border-erreur');
  });
});
