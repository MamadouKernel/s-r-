import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { SaleOperation } from '../data/journal';
import { CustomerService } from '../customers/customer.service';
import { CustomerSnapshot, PaymentDraft } from '../domain/types';
import { referenceKey } from '../domain/payment';
import { AuthService } from '../services/auth.service';
import { CashService } from '../services/cash.service';
import { NetworkService } from '../services/network.service';
import { SyncService } from '../services/sync.service';
import { Receipt } from '../core/receipt';
import { ReceiptService } from '../services/receipt.service';
import { PaymentPanel } from './payment-panel';

const sale = (draft: PaymentDraft[]): SaleOperation => ({
  kind: 'sale', seq: 2, status: 'pending', createdAt: 'x', saleId: 's', sessionId: 'sess', at: 'x', discountFcfa: 0,
  netTotalFcfa: 1450, changeFcfa: 0, lines: [], payments: draft,
});

const customer = (overrides: Partial<CustomerSnapshot> = {}): CustomerSnapshot => ({
  id: 'c-1', name: 'Awa Koné', phone: '+2250707070707', creditCeilingFcfa: 20_000, debtFcfa: 8_000, availableCreditFcfa: 12_000, isActive: true, ...overrides,
});

interface Options {
  used?: string[];
  pending?: number;
  customers?: CustomerSnapshot[];
  role?: string;
  online?: boolean;
  created?: CustomerSnapshot;
}

async function render(used: string[] = [], pending = 1, options: Omit<Options, 'used' | 'pending'> = {}) {
  const confirmSale = vi.fn(async (drafts: PaymentDraft[], customerId?: string | null) => ({
    ...sale(drafts), ...(customerId ? { customerId } : {}),
  }));
  const receipts = { remember: vi.fn(async (_sale: SaleOperation, _customer: string | null) => ({ commerce: 'Supérette' }) as Receipt), last: signal<Receipt | null>(null) };
  const customersSignal = signal(options.customers ?? []);
  const customerService = {
    customers: customersSignal.asReadonly(),
    loadLocal: vi.fn(async (term?: string) => {
      const needle = (term ?? '').toLowerCase();
      customersSignal.set((options.customers ?? []).filter((item) => item.name.toLowerCase().includes(needle)));
    }),
    refresh: vi.fn(async () => undefined),
    create: vi.fn(async () => options.created ?? customer({ id: 'c-new', name: 'Nouveau', debtFcfa: 0, availableCreditFcfa: 20_000 })),
  };
  TestBed.configureTestingModule({
    providers: [
      { provide: ReceiptService, useValue: receipts },
      { provide: SyncService, useValue: { counts: signal({ pending, synced: 0, rejected: 0 }) } },
      { provide: CustomerService, useValue: customerService },
      { provide: AuthService, useValue: { user: signal({ id: 'u', displayName: 'Awa', role: options.role ?? 'Cashier' }) } },
      { provide: NetworkService, useValue: { online: signal(options.online ?? true) } },
      {
        provide: CashService,
        useValue: { confirmSale, usedReferences: async () => new Set(used), submitting: signal(false) },
      },
    ],
  });
  const fixture: ComponentFixture<PaymentPanel> = TestBed.createComponent(PaymentPanel);
  fixture.componentRef.setInput('total', 1450);
  await fixture.whenStable();
  const root = fixture.nativeElement as HTMLElement;
  const type = async (element: HTMLInputElement, value: string) => {
    element.value = value;
    element.dispatchEvent(new Event('input'));
    await fixture.whenStable();
  };
  const click = async (label: string) => {
    const button = [...root.querySelectorAll('button')].find((candidate) => candidate.textContent?.includes(label));
    button?.click();
    await fixture.whenStable();
  };
  const choose = async (selector: string, value: string) => {
    const element = root.querySelector<HTMLSelectElement>(selector)!;
    element.value = value;
    element.dispatchEvent(new Event('change'));
    await fixture.whenStable();
  };
  return { fixture, root, confirmSale, customerService, receipts, type, click, choose };
}

describe('PaymentPanel', () => {
  it('starts with one cash payment covering the whole total', async () => {
    const { root } = await render();
    expect(root.textContent).toContain('Montant complet');
    expect((root.querySelector('select') as HTMLSelectElement).value).toBe('Cash');
  });

  it('shows Orange Money in the mode list of a second payment, as it is in the data', async () => {
    const { root, type, click } = await render();
    await type(root.querySelectorAll<HTMLInputElement>('input')[0], '1000');
    await click('Ajouter un paiement');

    const selects = root.querySelectorAll<HTMLSelectElement>('select');
    expect(selects).toHaveLength(2);
    expect(selects[1].value).toBe('OrangeMoney');
    expect(root.textContent).toContain('Référence de la transaction');
  });

  it('validates a mixed payment with its reference and sends the exact drafts', async () => {
    const { root, type, click, confirmSale } = await render();
    await type(root.querySelectorAll<HTMLInputElement>('input')[0], '1000');
    await click('Ajouter un paiement');
    const reference = root.querySelector<HTMLInputElement>('input.font-mono')!;
    await type(reference, 'MP-77');

    await click('Valider la vente');

    expect(confirmSale).toHaveBeenCalledTimes(1);
    const drafts = confirmSale.mock.calls[0][0];
    expect(drafts).toMatchObject([
      { method: 'Cash', amountFcfa: 1000, tenderedFcfa: 1000 },
      { method: 'OrangeMoney', amountFcfa: 450, reference: 'MP-77' },
    ]);
    expect(root.textContent).toContain('Vente enregistrée');
    expect(root.textContent).toContain('en attente de synchronisation');
  });

  it('says the sale reached the server once nothing is pending any more', async () => {
    const { root, click } = await render([], 0);
    await click('Valider la vente');
    expect(root.textContent).toContain('Enregistrée et envoyée au serveur');
    expect(root.textContent).not.toContain('en attente de synchronisation');
  });

  it('refuses to validate while the total is not covered and explains why', async () => {
    const { root, type, click, confirmSale } = await render();
    await type(root.querySelectorAll<HTMLInputElement>('input')[0], '1000');

    await click('Valider la vente');

    expect(confirmSale).not.toHaveBeenCalled();
    expect(root.querySelector('[role="alert"]')?.textContent).toContain('total des paiements');
    expect(root.textContent).toContain('Reste à payer');
  });

  it('refuses a reference already used on the device (SC-P5)', async () => {
    const { fixture, root, type, click, confirmSale } = await render([referenceKey('OrangeMoney', 'MP-9')]);
    const select = root.querySelector('select') as HTMLSelectElement;
    select.value = 'OrangeMoney';
    select.dispatchEvent(new Event('change'));
    await fixture.whenStable();
    await type(root.querySelector<HTMLInputElement>('input.font-mono')!, 'mp-9');

    await click('Valider la vente');

    expect(confirmSale).not.toHaveBeenCalled();
    expect(root.querySelector('[role="alert"]')?.textContent).toContain('a déjà été utilisée');
  });

  it('shows the change to give when the client hands more cash', async () => {
    const { root, type } = await render();
    await type(root.querySelectorAll<HTMLInputElement>('input')[1], '2000');
    expect(root.textContent).toContain('Monnaie à rendre');
    expect(root.textContent).toContain('550');
  });
});

describe('PaymentPanel: credit (CRD-04)', () => {
  const awa = customer();

  async function onCredit(options: Omit<Options, 'used' | 'pending'> = { customers: [awa] }) {
    const page = await render([], 1, options);
    await page.choose('select', 'Credit');
    return page;
  }

  it('offers credit as a payment mode, in plain words, with nothing to type but the amount', async () => {
    const { root, choose } = await render([], 1, { customers: [awa] });

    expect(root.textContent).toContain('Crédit (le client paiera plus tard)');
    await choose('select', 'Credit');

    expect(root.textContent).toContain('Ce montant s’ajoute à ce que le client vous doit');
    expect(root.querySelector('input.font-mono')).toBeNull(); // pas de référence
    expect(root.textContent).not.toContain('Montant reçu');
  });

  it('shows no customer section until credit is chosen', async () => {
    const { root } = await render([], 1, { customers: [awa] });
    expect(root.textContent).not.toContain('Client à crédit');
  });

  it('refuses to validate without a customer and says why', async () => {
    const { root, click, confirmSale } = await onCredit();

    await click('Valider la vente');

    expect(confirmSale).not.toHaveBeenCalled();
    expect(root.textContent).toContain('Choisissez le client à qui vous vendez à crédit.');
  });

  it('lists the customers with their debt and available credit, and filters them while typing', async () => {
    const issa = customer({ id: 'c-2', name: 'Issa Diallo', debtFcfa: 0, availableCreditFcfa: 20_000 });
    const { root, type } = await onCredit({ customers: [awa, issa] });

    const text = root.textContent!.replace(/\u00a0/g, ' ');
    expect(text).toContain('Awa Koné');
    expect(text).toContain('Dette : 8 000 FCFA · Crédit disponible : 12 000 FCFA');
    expect(text).toContain('Issa Diallo');

    await type(root.querySelector<HTMLInputElement>('#client-recherche')!, 'issa');
    expect(root.textContent).not.toContain('Awa Koné');
    expect(root.textContent).toContain('Issa Diallo');
  });

  it('sells on credit for a customer within the ceiling and sends the customer with the sale (SC-U-credit)', async () => {
    const { fixture, root, click, confirmSale, type } = await render([], 1, { customers: [awa] });
    await type(root.querySelectorAll<HTMLInputElement>('input')[0], '1000');
    await click('Ajouter un paiement');
    const selects = root.querySelectorAll<HTMLSelectElement>('select');
    selects[1].value = 'Credit';
    selects[1].dispatchEvent(new Event('change'));
    await fixture.whenStable();
    await click('Awa Koné');

    await click('Valider la vente');

    expect(confirmSale).toHaveBeenCalledTimes(1);
    const [drafts, customerId] = confirmSale.mock.calls[0];
    expect(drafts).toMatchObject([{ method: 'Cash', amountFcfa: 1000 }, { method: 'Credit', amountFcfa: 450 }]);
    expect(customerId).toBe('c-1');
    expect(root.textContent!.replace(/\u00a0/g, ' ')).toContain('À crédit pour Awa Koné : 450 FCFA. Sa dette passe à 8 450 FCFA.');
  });

  it('stops a cashier above the ceiling with the figures and what to do, and sends nothing', async () => {
    const tight = customer({ debtFcfa: 19_000, availableCreditFcfa: 1_000 });
    const { root, click, confirmSale } = await onCredit({ customers: [tight], role: 'Cashier' });
    await click('Awa Koné');

    await click('Valider la vente');

    const alert = root.querySelector('[role="alert"]')!.textContent!.replace(/\u00a0/g, ' ');
    expect(alert).toContain('Awa Koné doit déjà 19 000 FCFA pour un plafond de 20 000 FCFA');
    expect(alert).toContain('il reste 1 000 FCFA de crédit');
    expect(alert).toContain('Seul un gérant peut valider un crédit plus grand : appelez-le.');
    expect(confirmSale).not.toHaveBeenCalled();
  });

  it('lets a manager go above the ceiling after telling them it is their validation', async () => {
    const tight = customer({ debtFcfa: 19_000, availableCreditFcfa: 1_000 });
    const { root, click, confirmSale } = await onCredit({ customers: [tight], role: 'Manager' });
    await click('Awa Koné');

    expect(root.textContent).toContain('En validant la vente, vous l’autorisez en tant que gérant');
    await click('Valider la vente');

    expect(confirmSale).toHaveBeenCalledTimes(1);
  });

  it('lets the cashier change the customer', async () => {
    const { root, click } = await onCredit();
    await click('Awa Koné');
    expect(root.textContent).toContain('Changer de client');

    await click('Changer de client');

    expect(root.querySelector('#client-recherche')).toBeTruthy();
  });

  it('creates a customer at the till and selects it, online', async () => {
    const created = customer({ id: 'c-new', name: 'Marie Awa', debtFcfa: 0, availableCreditFcfa: 20_000 });
    const { root, click, type, customerService } = await onCredit({ customers: [], created });

    expect(root.textContent).toContain('Aucun client enregistré sur cet appareil. Créez-le ci-dessous.');
    await click('Nouveau client');
    await type(root.querySelector<HTMLInputElement>('#client-nom')!, 'Marie Awa');
    await type(root.querySelector<HTMLInputElement>('#client-telephone')!, '07 07 07 07 07');
    await click('Enregistrer le client');

    expect(customerService.create).toHaveBeenCalledWith('Marie Awa', '07 07 07 07 07');
    expect(root.textContent).toContain('Marie Awa');
    expect(root.textContent).toContain('Changer de client');
  });

  it('asks for a name before creating a customer, and shows the server refusal in plain words', async () => {
    const page = await onCredit({ customers: [] });
    await page.click('Nouveau client');
    await page.click('Enregistrer le client');
    expect(page.root.textContent).toContain('Donnez le nom du client');
    expect(page.customerService.create).not.toHaveBeenCalled();

    page.customerService.create.mockRejectedValueOnce(Object.assign(new Error('x'), {}));
    await page.type(page.root.querySelector<HTMLInputElement>('#client-nom')!, 'Awa');
    await page.click('Enregistrer le client');
    expect(page.root.querySelector('[role="alert"]')?.textContent).toContain('Une erreur est survenue');
  });

  it('works offline with the list kept on the device, but cannot create a customer without internet', async () => {
    const { root } = await onCredit({ customers: [awa], online: false });

    expect(root.textContent).toContain('Awa Koné');
    const newCustomer = [...root.querySelectorAll('button')].find((button) => button.textContent?.includes('Nouveau client'))!;
    expect(newCustomer.disabled).toBe(true);
    expect(root.textContent).toContain('Ajouter un client demande internet.');
  });

  it('says so when the device has no customer list yet and is offline', async () => {
    const { root } = await onCredit({ customers: [], online: false });
    expect(root.textContent).toContain('Connectez-vous à internet pour charger la liste des clients');
  });

  it('refreshes the list from the server when opened online, and not offline', async () => {
    const online = await render([], 1, { customers: [awa], online: true });
    expect(online.customerService.refresh).toHaveBeenCalledOnce();

    TestBed.resetTestingModule();
    const offline = await render([], 1, { customers: [awa], online: false });
    expect(offline.customerService.refresh).not.toHaveBeenCalled();
  });

  it('sends no customer for a sale without credit', async () => {
    const { click, confirmSale } = await render([], 1, { customers: [awa] });

    await click('Valider la vente');

    expect(confirmSale.mock.calls[0][1]).toBeNull();
  });
});


/** Laisse finir les attentes en chaîne de la validation (enregistrement de la vente, puis préparation du ticket). */
async function later(fixture: ComponentFixture<PaymentPanel>): Promise<void> {
  await new Promise((resolve) => setTimeout(resolve, 10));
  await fixture.whenStable();
  fixture.detectChanges();
}

describe('PaymentPanel: ticket (docs/specs/etiquettes-tickets TKT-01)', () => {
  it('offers to print, share or copy the ticket once the sale is recorded, next to « Nouvelle vente »', async () => {
    const { root, click, receipts, fixture } = await render();

    await click('Valider la vente');
    await later(fixture);

    expect(receipts.remember).toHaveBeenCalledOnce();
    expect(root.textContent).toContain('Imprimer le ticket');
    expect(root.textContent).toContain('Copier le ticket');
    expect(root.textContent).toContain('Nouvelle vente');
  });

  it('builds the ticket from the recorded sale, with no customer for a cash sale', async () => {
    const { click, receipts, fixture } = await render();

    await click('Valider la vente');
    await later(fixture);

    const [recorded, customerName] = receipts.remember.mock.calls[0];
    expect(recorded.netTotalFcfa).toBe(1450);
    expect(customerName).toBeNull();
  });

  it('gives the name of the customer to the ticket of a credit sale', async () => {
    const { root, click, type, receipts, fixture } = await render([], 1, { customers: [customer()] });
    await type(root.querySelectorAll<HTMLInputElement>('input')[0], '1000');
    await click('Ajouter un paiement');
    const selects = root.querySelectorAll<HTMLSelectElement>('select');
    selects[1].value = 'Credit';
    selects[1].dispatchEvent(new Event('change'));
    await fixture.whenStable();
    await click('Awa Koné');

    await click('Valider la vente');
    await later(fixture);

    expect(receipts.remember.mock.calls[0][1]).toBe('Awa Koné');
  });

  it('never blocks the sale when the ticket cannot be built', async () => {
    const { root, click, receipts, fixture } = await render();
    receipts.remember.mockRejectedValue(new Error('journal indisponible'));

    await click('Valider la vente');
    await later(fixture);

    expect(root.textContent).toContain('Vente enregistrée');
    expect(root.textContent).toContain('Nouvelle vente');
    expect(root.textContent).not.toContain('Imprimer le ticket');
  });
});
