import { bannerFor } from './banner';
import { SubscriptionView } from './subscription-api';

const NOW = new Date(2026, 9, 25, 10, 0, 0); // 25/10/2026, 10 h, heure locale

function view(overrides: Partial<SubscriptionView> = {}): SubscriptionView {
  return {
    status: 'Trial', endsAt: new Date(2026, 9, 30, 8, 0, 0).toISOString(), readOnlyFrom: new Date(2026, 10, 6, 8, 0, 0).toISOString(), daysLeft: 5,
    isReadOnly: false, monthlyPriceFcfa: 5000, annualPriceFcfa: 50000, payNumbers: { Wave: '07 00 00 00 01' }, payments: [], ...overrides,
  };
}

describe('bannerFor', () => {
  it('says nothing when there is more than a week left', () => {
    expect(bannerFor(view({ status: 'Active', daysLeft: 20 }), 'Owner', NOW)).toBeNull();
    expect(bannerFor(view({ daysLeft: 8 }), 'Owner', NOW)).toBeNull();
  });

  it('says nothing when the state is not known yet', () => {
    expect(bannerFor(null, 'Owner', NOW)).toBeNull();
  });

  it('warns the owner of the end of the trial with the days left and the date, with a way to pay', () => {
    const banner = bannerFor(view({ daysLeft: 5 }), 'Owner', NOW)!;

    expect(banner.tone).toBe('info');
    expect(banner.text).toBe('Votre essai gratuit finit dans 5 jours (le 30/10).');
    expect(banner.action).toEqual({ label: 'Voir mon abonnement', link: '/gestion/abonnement' });
  });

  it('talks about the subscription once something was paid, and says one day in the singular', () => {
    const banner = bannerFor(view({ status: 'Active', daysLeft: 1, endsAt: new Date(2026, 9, 26, 8, 0, 0).toISOString() }), 'Owner', NOW)!;

    expect(banner.text).toBe('Votre abonnement se termine demain (le 26/10).');
    expect(banner.tone).toBe('warn');
  });

  it('says today on the last day', () => {
    const banner = bannerFor(view({ status: 'Active', daysLeft: 1, endsAt: new Date(2026, 9, 25, 18, 0, 0).toISOString() }), 'Manager', NOW)!;

    expect(banner.text).toBe('Votre abonnement se termine aujourd’hui. Payez pour ne rien interrompre.');
  });

  it('counts the days before the read-only mode during the grace, and sends the owner to pay', () => {
    const banner = bannerFor(view({ status: 'Grace', daysLeft: 3 }), 'Owner', NOW)!;

    expect(banner.tone).toBe('warn');
    expect(banner.text).toBe('Abonnement échu. Plus que 3 jours avant la lecture seule (le 06/11).');
    expect(banner.action).toEqual({ label: 'Payer maintenant', link: '/gestion/abonnement' });
  });

  it('says the data is kept in read-only mode', () => {
    const banner = bannerFor(view({ status: 'ReadOnly', isReadOnly: true, daysLeft: 0 }), 'Owner', NOW)!;

    expect(banner.tone).toBe('error');
    expect(banner.text).toContain('Lecture seule');
    expect(banner.text).toContain('vos données sont conservées');
    expect(banner.action?.label).toBe('Payer pour reprendre');
    expect(banner.secondaryAction).toEqual({ label: 'Récupérer mes données', link: '/gestion/export' });
  });

  it('shows the manager the same message but only the owner can pay', () => {
    const grace = bannerFor(view({ status: 'Grace', daysLeft: 2 }), 'Manager', NOW)!;
    const readOnly = bannerFor(view({ status: 'ReadOnly', isReadOnly: true, daysLeft: 0 }), 'Manager', NOW)!;

    expect(grace.action).toEqual({ label: 'Voir l’abonnement', link: '/gestion/abonnement' });
    expect(readOnly.text).toContain('Prévenez le propriétaire');
    expect(readOnly.secondaryAction ?? null).toBeNull(); // l'export est réservé au propriétaire
  });

  it.each(['Cashier', 'StockKeeper', 'Accountant'])('shows %s a short message without price or button, and only once it matters', (role) => {
    expect(bannerFor(view({ daysLeft: 3 }), role, NOW)).toBeNull();

    const grace = bannerFor(view({ status: 'Grace', daysLeft: 3 }), role, NOW)!;
    const readOnly = bannerFor(view({ status: 'ReadOnly', isReadOnly: true, daysLeft: 0 }), role, NOW)!;

    expect(grace.text).toBe('L’abonnement de la boutique est échu. Prévenez votre responsable.');
    expect(grace.action).toBeNull();
    expect(readOnly.text).toBe('La boutique est en lecture seule. Les ventes en cours sont enregistrées ; prévenez votre responsable.');
    expect(readOnly.action).toBeNull();
  });
});
