import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { CustomerSnapshot } from '../domain/types';
import { AuthService } from '../services/auth.service';
import { NetworkService } from '../services/network.service';
import { ApiError } from '../sync/sync-api';
import { CustomersApi } from './customers-api';
import { CustomersPage } from './customers-page';

const customer = (overrides: Partial<CustomerSnapshot> = {}): CustomerSnapshot => ({
  id: 'c-1', name: 'Awa Koné', phone: '+2250707070707', creditCeilingFcfa: 20_000, debtFcfa: 8_000, availableCreditFcfa: 12_000, isActive: true, ...overrides,
});

async function open(customers: CustomerSnapshot[] = [customer()], options: { role?: string; online?: boolean } = {}) {
  const api = {
    list: vi.fn().mockResolvedValue(customers),
    create: vi.fn().mockResolvedValue({ customerId: 'c-new', alreadyProcessed: false, creditCeilingFcfa: 20_000 }),
    update: vi.fn().mockResolvedValue(customer()),
    deactivate: vi.fn().mockResolvedValue(customer()),
    repay: vi.fn().mockResolvedValue({ repaymentId: 'r', debtFcfa: 3_000, alreadyProcessed: false }),
    repayments: vi.fn().mockResolvedValue([]),
  };
  TestBed.resetTestingModule();
  TestBed.configureTestingModule({
    providers: [
      { provide: CustomersApi, useValue: api },
      { provide: AuthService, useValue: { user: signal({ id: 'u', displayName: 'X', role: options.role ?? 'Manager' }) } },
      { provide: NetworkService, useValue: { online: signal(options.online ?? true) } },
    ],
  });
  const fixture = TestBed.createComponent(CustomersPage);
  const root = fixture.nativeElement as HTMLElement;
  const settle = async () => {
    await fixture.whenStable();
    await new Promise((resolve) => setTimeout(resolve, 5));
    await fixture.whenStable();
  };
  await settle();
  const norm = () => (root.textContent ?? '').replace(/ /g, ' ');
  return {
    api, root, settle, text: norm,
    has: (value: string) => norm().includes(value),
    click: async (label: string) => {
      const target = [...root.querySelectorAll('button')].find((b) => (b.textContent ?? '').includes(label) || b.getAttribute('aria-label')?.includes(label));
      expect(target, `bouton « ${label} » introuvable`).toBeTruthy();
      target!.click();
      await settle();
    },
    buttons: () => [...root.querySelectorAll('button')].map((b) => b.getAttribute('aria-label') ?? b.textContent?.trim() ?? ''),
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
    submit: async () => {
      root.querySelector('form:not([role="search"])')!.dispatchEvent(new Event('submit', { cancelable: true }));
      await settle();
    },
  };
}

describe('CustomersPage', () => {
  it('lists customers with their debt in words, their ceiling and available credit, and the total owed', async () => {
    const page = await open([customer(), customer({ id: 'c-2', name: 'Issa Diallo', phone: null, debtFcfa: 0, availableCreditFcfa: 20_000 })]);

    expect(page.has('Awa Koné')).toBe(true);
    expect(page.has('Doit 8 000 FCFA')).toBe(true);
    expect(page.has('Plafond 20 000 FCFA · Crédit disponible 12 000 FCFA')).toBe(true);
    expect(page.has('Issa Diallo')).toBe(true);
    expect(page.has('À jour')).toBe(true);
    expect(page.has('Pas de téléphone')).toBe(true);
    expect(page.root.querySelector('#clients-total')?.textContent?.replace(/\s+/g, ' ').replace(/ /g, ' ')).toContain('Dettes à recouvrer : 8 000 FCFA chez 1 client');
  });

  it('says what to do when there is no customer yet, and when a search finds nobody', async () => {
    const empty = await open([]);
    expect(empty.has('Aucun client pour le moment')).toBe(true);

    const page = await open([]);
    await page.type('#clients-recherche', 'zzz');
    await new Promise((resolve) => setTimeout(resolve, 300));
    await page.settle();
    expect(page.has('Aucun client ne correspond à cette recherche.')).toBe(true);
  });

  it('searches on the server after a short pause while typing', async () => {
    const page = await open();
    await page.type('#clients-recherche', 'awa');
    await new Promise((resolve) => setTimeout(resolve, 300));

    expect(page.api.list).toHaveBeenLastCalledWith('awa');
  });

  it('shows a loading failure in French with a retry', async () => {
    const page = await open();
    page.api.list.mockRejectedValue(new ApiError(0, 'network', ''));
    await page.type('#clients-recherche', 'x');
    await new Promise((resolve) => setTimeout(resolve, 300));
    await page.settle();

    expect(page.root.querySelector('[role="alert"]')?.textContent).toContain('injoignable');
    page.api.list.mockResolvedValue([customer()]);
    await page.click('Réessayer');
    expect(page.has('Awa Koné')).toBe(true);
  });

  it('collects a partial repayment, shows what remains and refreshes (SC-C5)', async () => {
    const page = await open();

    await page.click('Encaisser un versement de Awa Koné');
    expect(page.has('Au plus 8 000 FCFA, ce que le client doit')).toBe(true);
    await page.type('#versement-montant', '5000');
    page.api.list.mockResolvedValue([customer({ debtFcfa: 3_000, availableCreditFcfa: 17_000 })]);
    await page.submit();

    expect(page.api.repay).toHaveBeenCalledWith('c-1', { repaymentId: expect.stringMatching(/^[0-9a-f-]{36}$/), amountFcfa: 5000, method: 'Cash', reference: null });
    expect(page.root.querySelector('[role="status"]')?.textContent?.replace(/\u00a0/g, ' ')).toContain('Versement enregistré. Il reste 3 000 FCFA à payer à Awa Koné.');
    expect(page.has('Doit 3 000 FCFA')).toBe(true);
  });

  it('says the customer owes nothing any more after the last repayment', async () => {
    const page = await open();
    page.api.repay.mockResolvedValue({ repaymentId: 'r', debtFcfa: 0, alreadyProcessed: false });
    await page.click('Encaisser un versement de Awa Koné');
    await page.type('#versement-montant', '8000');
    page.api.list.mockResolvedValue([customer({ debtFcfa: 0, availableCreditFcfa: 20_000 })]);

    await page.submit();

    expect(page.root.querySelector('[role="status"]')?.textContent).toContain('Awa Koné ne doit plus rien.');
    expect(page.buttons().filter((label) => label.startsWith('Encaisser'))).toEqual([]);
  });

  it('refuses a repayment that is zero, over the debt, or a mobile money one without reference', async () => {
    const page = await open();
    await page.click('Encaisser un versement de Awa Koné');

    await page.type('#versement-montant', '0');
    await page.submit();
    expect(page.has('supérieur à zéro')).toBe(true);

    await page.type('#versement-montant', '8001');
    await page.submit();
    expect(page.has('dépasse ce que le client doit (8 000 FCFA)')).toBe(true);

    await page.type('#versement-montant', '1000');
    await page.choose('#versement-mode', 'Wave');
    await page.submit();
    expect(page.has('La référence de la transaction est obligatoire')).toBe(true);
    expect(page.api.repay).not.toHaveBeenCalled();
  });

  it('sends the mobile money reference and keeps the same repayment id when retried after a network failure', async () => {
    const page = await open();
    page.api.repay.mockRejectedValueOnce(new ApiError(0, 'network', ''));
    await page.click('Encaisser un versement de Awa Koné');
    await page.type('#versement-montant', '1000');
    await page.choose('#versement-mode', 'OrangeMoney');
    await page.type('#versement-reference', ' OM-7 ');

    await page.submit();
    expect(page.root.querySelector('[role="alert"]')?.textContent).toContain('injoignable');
    await page.submit();

    const [first, second] = page.api.repay.mock.calls;
    expect(first[1].repaymentId).toBe(second[1].repaymentId);
    expect(second[1]).toMatchObject({ method: 'OrangeMoney', reference: 'OM-7' });
  });

  it('offers the right actions to each profile', async () => {
    const manager = await open([customer(), customer({ id: 'c-2', name: 'Issa Diallo', debtFcfa: 0, availableCreditFcfa: 20_000 })], { role: 'Manager' });
    expect(manager.buttons()).toContain('Modifier Awa Koné');
    expect(manager.buttons()).toContain('Historique de Awa Koné');
    expect(manager.buttons()).toContain('Encaisser un versement de Awa Koné');
    expect(manager.buttons()).not.toContain('Désactiver Awa Koné'); // une dette l'interdit
    expect(manager.buttons()).toContain('Désactiver Issa Diallo');

    const cashier = await open([customer()], { role: 'Cashier' });
    expect(cashier.buttons()).toContain('Encaisser un versement de Awa Koné');
    expect(cashier.buttons()).not.toContain('Modifier Awa Koné');
    expect(cashier.buttons()).not.toContain('Historique de Awa Koné');
    expect(cashier.buttons()).toContain('Ajouter un client');

    const accountant = await open([customer()], { role: 'Accountant' });
    expect(accountant.buttons()).toContain('Historique de Awa Koné');
    expect(accountant.buttons()).not.toContain('Encaisser un versement de Awa Koné');
    expect(accountant.buttons()).not.toContain('Ajouter un client');
  });

  it('adds a customer; only a manager sees the ceiling field (SC-C1, SC-C10)', async () => {
    const cashier = await open([], { role: 'Cashier' });
    await cashier.click('Ajouter un client');
    expect(cashier.root.querySelector('#ajout-plafond')).toBeNull();
    await cashier.type('#ajout-nom', ' Marie Awa ');
    await cashier.type('#ajout-telephone', '07 07 07 07 07');
    await cashier.submit();
    expect(cashier.api.create).toHaveBeenCalledWith({ customerId: expect.stringMatching(/^[0-9a-f-]{36}$/), name: 'Marie Awa', phone: '07 07 07 07 07', creditCeilingFcfa: null });
    expect(cashier.root.querySelector('[role="status"]')?.textContent).toContain('Client ajouté : Marie Awa.');

    const manager = await open([], { role: 'Manager' });
    await manager.click('Ajouter un client');
    await manager.type('#ajout-nom', 'Issa Diallo');
    await manager.type('#ajout-plafond', '50000');
    await manager.submit();
    expect(manager.api.create).toHaveBeenCalledWith(expect.objectContaining({ name: 'Issa Diallo', creditCeilingFcfa: 50_000 }));
  });

  it('refuses a name that is too short and a ceiling that is not an amount, and shows the phone conflict in plain words', async () => {
    const page = await open([], { role: 'Manager' });
    await page.click('Ajouter un client');
    await page.type('#ajout-nom', 'A');
    await page.submit();
    expect(page.has('Donnez le nom du client')).toBe(true);

    await page.type('#ajout-nom', 'Awa Koné');
    await page.type('#ajout-plafond', 'beaucoup');
    await page.submit();
    expect(page.has('Le plafond doit être un montant entier en FCFA.')).toBe(true);

    await page.type('#ajout-plafond', '');
    page.api.create.mockRejectedValue(new ApiError(409, 'phone_taken', 'Le numéro +225 07 ** ** ** 07 est déjà celui d’un autre client.'));
    await page.submit();
    expect(page.root.querySelector('[role="alert"]')?.textContent).toContain('est déjà celui d’un autre client');
  });

  it('edits the name, the phone and the ceiling', async () => {
    const page = await open([customer()], { role: 'Manager' });

    await page.click('Modifier Awa Koné');
    expect(page.root.querySelector<HTMLInputElement>('#edition-plafond')!.value).toBe('20000');
    await page.type('#edition-nom', 'Awa Koné-Traoré');
    await page.type('#edition-plafond', '50000');
    await page.submit();

    expect(page.api.update).toHaveBeenCalledWith('c-1', { name: 'Awa Koné-Traoré', phone: '+2250707070707', creditCeilingFcfa: 50_000 });
    expect(page.root.querySelector('[role="status"]')?.textContent).toContain('Client modifié : Awa Koné-Traoré.');
  });

  it('asks for a confirmation before deactivating, and keeps the customer if the manager says no (SC-C9)', async () => {
    const page = await open([customer({ debtFcfa: 0, availableCreditFcfa: 20_000 })], { role: 'Manager' });

    await page.click('Désactiver Awa Koné');
    expect(page.has('Désactiver Awa Koné ? Il ne sera plus proposé à la caisse. Son historique est conservé.')).toBe(true);
    await page.click('Non, le garder');
    expect(page.api.deactivate).not.toHaveBeenCalled();

    await page.click('Désactiver Awa Koné');
    page.api.list.mockResolvedValue([]);
    await page.click('Oui, désactiver');
    expect(page.api.deactivate).toHaveBeenCalledWith('c-1');
    expect(page.root.querySelector('[role="status"]')?.textContent).toContain('Client désactivé : Awa Koné.');
  });

  it('shows the repayment history with the amount, the way and the reference', async () => {
    const page = await open([customer()], { role: 'Manager' });
    page.api.repayments.mockResolvedValue([
      { id: 'r1', amountFcfa: 5_000, method: 'Wave', reference: 'WV-1', userId: 'u', at: '2026-10-22T10:00:00.000Z' },
      { id: 'r2', amountFcfa: 1_000, method: 'Cash', reference: null, userId: 'u', at: '2026-10-20T10:00:00.000Z' },
    ]);

    await page.click('Historique de Awa Koné');

    expect(page.has('5 000 FCFA')).toBe(true);
    expect(page.has('Wave · WV-1')).toBe(true);
    expect(page.has('Espèces')).toBe(true);
  });

  it('says there is no repayment yet', async () => {
    const page = await open([customer()], { role: 'Manager' });
    await page.click('Historique de Awa Koné');
    expect(page.has('Aucun versement pour ce client.')).toBe(true);
  });

  it('needs the network for everything that writes, and says so', async () => {
    const page = await open([customer()], { role: 'Manager', online: false });

    expect(page.has('Vous êtes hors ligne')).toBe(true);
    const disabled = [...page.root.querySelectorAll('button')].filter((b) => b.disabled).map((b) => b.getAttribute('aria-label') ?? b.textContent?.trim());
    expect(disabled).toEqual(expect.arrayContaining(['Ajouter un client', 'Encaisser un versement de Awa Koné', 'Modifier Awa Koné']));
  });

  it('never shows an identifier', async () => {
    const page = await open([customer({ id: '0a1b2c3d-1111-2222-3333-444455556666' })]);
    expect(page.has('0a1b2c3d')).toBe(false);
  });
});
