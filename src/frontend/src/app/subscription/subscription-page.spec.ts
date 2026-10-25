import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { ApiError } from '../sync/sync-api';
import { AuthService } from '../services/auth.service';
import { NetworkService } from '../services/network.service';
import { SubscriptionApi, SubscriptionPayment, SubscriptionView } from './subscription-api';
import { SubscriptionPage } from './subscription-page';
import { SubscriptionService } from './subscription.service';

const view = (overrides: Partial<SubscriptionView> = {}): SubscriptionView => ({
  status: 'Trial', endsAt: '2026-10-30T08:00:00', readOnlyFrom: '2026-11-06T08:00:00', daysLeft: 5, isReadOnly: false,
  monthlyPriceFcfa: 5000, annualPriceFcfa: 50000,
  payNumbers: { Wave: '07 00 00 00 01', OrangeMoney: '05 00 00 00 02' }, payments: [], ...overrides,
});

const payment = (overrides: Partial<SubscriptionPayment> = {}): SubscriptionPayment => ({
  id: 'p1', period: 'Monthly', method: 'Wave', reference: 'W-123456', amountFcfa: 5000, status: 'Pending', declaredAt: '2026-10-25T10:00:00',
  decidedAt: null, reason: null, coveredTo: null, ...overrides,
});

async function open(current: SubscriptionView | null, role = 'Owner', online = true) {
  const state = signal<SubscriptionView | null>(current);
  const refresh = vi.fn().mockResolvedValue(undefined);
  const declare = vi.fn().mockResolvedValue(undefined);
  TestBed.configureTestingModule({
    providers: [
      provideRouter([]),
      { provide: SubscriptionApi, useValue: { declare } },
      { provide: SubscriptionService, useValue: { view: state, refresh } },
      { provide: AuthService, useValue: { user: signal({ id: 'u', displayName: 'Awa', role }) } },
      { provide: NetworkService, useValue: { online: signal(online) } },
    ],
  });
  const fixture = TestBed.createComponent(SubscriptionPage);
  const root = fixture.nativeElement as HTMLElement;
  const settle = async () => {
    await fixture.whenStable();
    await new Promise((resolve) => setTimeout(resolve, 5));
    await fixture.whenStable();
  };
  await settle();
  return {
    root, declare, refresh, state, settle,
    has: (text: string) => (root.textContent ?? '').replace(/\s+/g, ' ').includes(text),
    type: async (selector: string, value: string) => {
      const input = root.querySelector<HTMLInputElement>(selector)!;
      input.value = value;
      input.dispatchEvent(new Event('input'));
      await fixture.whenStable();
    },
    click: async (label: string) => {
      const button = [...root.querySelectorAll('button')].find((candidate) => (candidate.textContent ?? '').includes(label));
      expect(button, `bouton « ${label} » introuvable`).toBeTruthy();
      button!.click();
      await settle();
    },
    submit: async () => {
      root.querySelector('form')!.dispatchEvent(new Event('submit', { cancelable: true }));
      await settle();
    },
  };
}

describe('SubscriptionPage', () => {
  it('shows the trial state in words, with the days left and the end date', async () => {
    const page = await open(view());

    expect(page.has('Essai gratuit')).toBe(true);
    expect(page.has('Plus que 5 jours')).toBe(true);
    expect(page.has('Votre essai se termine le 30/10/2026')).toBe(true);
    expect(page.has('vos jours restants ne sont pas perdus')).toBe(true);
  });

  it.each([
    [{ status: 'Active', daysLeft: 20, endsAt: '2026-11-30T08:00:00' } as Partial<SubscriptionView>, 'Abonnement actif', 'Jusqu’au 30/11/2026'],
    [{ status: 'Grace', daysLeft: 3 } as Partial<SubscriptionView>, 'Abonnement échu', 'Plus que 3 jours avant la lecture seule'],
    [{ status: 'ReadOnly', isReadOnly: true, daysLeft: 0 } as Partial<SubscriptionView>, 'Lecture seule', 'Vos données sont conservées'],
  ])('describes the state %j', async (overrides, title, tag) => {
    const page = await open(view(overrides));

    expect(page.has(title)).toBe(true);
    expect(page.has(tag)).toBe(true);
  });

  it('explains how to pay in three steps with the amount and the number of the chosen method', async () => {
    const page = await open(view());

    expect(page.has('1. Choisissez la durée')).toBe(true);
    expect(page.has('2. Payez avec votre téléphone')).toBe(true);
    expect(page.has('3. Déclarez votre paiement')).toBe(true);
    expect(page.has('Envoyez 5 000 FCFA par Wave au numéro')).toBe(true);
    expect(page.has('07 00 00 00 01')).toBe(true);

    await page.click('Orange Money');
    expect(page.has('par Orange Money au numéro')).toBe(true);
    expect(page.has('05 00 00 00 02')).toBe(true);
    expect(page.has('MTN MoMo')).toBe(false);
  });

  it('switches the amount to the annual price and says how many months are offered', async () => {
    const page = await open(view());
    expect(page.has('2 mois offerts')).toBe(true);

    await page.click('12 mois');

    expect(page.has('Envoyez 50 000 FCFA')).toBe(true);
    expect(page.root.querySelector<HTMLInputElement>('#montant')!.value).toBe('50000');
  });

  it('declares the payment with the chosen duration and method, trimmed, then confirms and clears the reference', async () => {
    const page = await open(view());
    await page.click('12 mois');
    await page.click('Orange Money');
    await page.type('#reference', '  MP2610.1234 ');

    await page.submit();

    expect(page.declare).toHaveBeenCalledTimes(1);
    const input = page.declare.mock.calls[0][0];
    expect(input).toMatchObject({ period: 'Annual', method: 'OrangeMoney', reference: 'MP2610.1234', amountFcfa: 50000 });
    expect(input.paymentId).toMatch(/^[0-9a-f-]{36}$/);
    expect(page.has('Déclaration envoyée.')).toBe(true);
    expect(page.root.querySelector<HTMLInputElement>('#reference')!.value).toBe('');
    expect(page.refresh).toHaveBeenCalled();
  });

  it('asks for the reference and refuses an amount below the price, with the price in the message', async () => {
    const page = await open(view());
    await page.type('#montant', '4000');

    await page.submit();

    expect(page.declare).not.toHaveBeenCalled();
    expect(page.root.querySelector('#reference-erreur')?.textContent).toContain('obligatoire');
    expect(page.root.querySelector('#montant-erreur')?.textContent?.replace(/\s+/g, ' ')).toContain('l’abonnement mensuel coûte 5 000 FCFA');
  });

  it('accepts an amount above the price and refuses one that is not a number', async () => {
    const page = await open(view());
    await page.type('#reference', 'W-1111');
    await page.type('#montant', '12,5');
    await page.submit();
    expect(page.root.querySelector('#montant-erreur')?.textContent).toContain('en chiffres');

    await page.type('#montant', '6000');
    await page.submit();

    expect(page.declare.mock.calls[0][0].amountFcfa).toBe(6000);
  });

  it('shows the server message when the reference was already used and keeps the form for a retry with the same identifier', async () => {
    const page = await open(view());
    page.declare.mockRejectedValueOnce(new ApiError(409, 'mobile_money_reference_used', 'Cette référence de transaction a déjà été utilisée.'));
    await page.type('#reference', 'W-USED01');

    await page.submit();
    expect(page.root.querySelector('[role="alert"]')?.textContent).toContain('déjà été utilisée');
    expect(page.root.querySelector<HTMLInputElement>('#reference')!.value).toBe('W-USED01');
    await page.type('#reference', 'W-USED02');
    await page.submit();

    expect(page.declare).toHaveBeenCalledTimes(2);
    expect(page.declare.mock.calls[1][0].paymentId).toBe(page.declare.mock.calls[0][0].paymentId);
  });

  it('shows each payment of the history with its state in words and the reason of a refusal', async () => {
    const page = await open(view({
      payments: [
        payment({ id: 'a', status: 'Pending' }),
        payment({ id: 'b', status: 'Validated', coveredTo: '2026-11-30T08:00:00', reference: 'W-OK0001' }),
        payment({ id: 'c', status: 'Refused', reason: 'Référence introuvable sur le relevé.', reference: 'W-BAD001' }),
      ],
    }));

    const rows = [...page.root.querySelectorAll('tbody tr')].map((row) => (row.textContent ?? '').replace(/\s+/g, ' '));
    expect(rows).toHaveLength(3);
    expect(rows[0]).toContain('En attente de validation');
    expect(rows[1]).toContain('Validé · couvre jusqu’au 30/11');
    expect(rows[2]).toContain('Refusé');
    expect(rows[2]).toContain('Référence introuvable sur le relevé.');
  });

  it('says there is no payment yet', async () => {
    const page = await open(view());

    expect(page.has('Aucun paiement pour le moment.')).toBe(true);
  });

  it('lets a manager read the state, the price and the history but not declare a payment', async () => {
    const page = await open(view({ payments: [payment()] }), 'Manager');

    expect(page.root.querySelector('form')).toBeNull();
    expect(page.has('Seul le propriétaire peut déclarer un paiement.')).toBe(true);
    expect(page.has('5 000 FCFA par mois')).toBe(true);
    expect(page.root.querySelectorAll('tbody tr')).toHaveLength(1);
  });

  it('says it cannot read the subscription when there is nothing known, and offers a retry', async () => {
    const page = await open(null);

    expect(page.root.querySelector('[role="alert"]')?.textContent).toContain('Impossible de lire votre abonnement');
    await page.click('Réessayer');
    expect(page.refresh).toHaveBeenCalledTimes(2);
  });

  it('warns when offline', async () => {
    const page = await open(view(), 'Owner', false);

    expect(page.has('Vous êtes hors ligne')).toBe(true);
  });
});
