import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { AuthService } from '../services/auth.service';
import { CashService } from '../services/cash.service';
import { NetworkService } from '../services/network.service';
import { ApiError } from '../sync/sync-api';
import { ReversalDone, SaleDetail, SalesApi } from './sales-api';
import { SaleDetailPage } from './sale-detail-page';

const detail = (overrides: Partial<SaleDetail> = {}): SaleDetail => ({
  id: 's-1', ticket: 12, recordedAt: '2026-10-26T08:05:00.000Z', cashierId: 'u-awa', registerId: 'reg-1', sessionId: 'sess',
  grossTotalFcfa: 3200, discountFcfa: 200, netTotalFcfa: 3000, status: 'Validated',
  lines: [
    { productId: 'p-jus', productName: "Jus d'orange 1L", quantity: 2, unitPriceFcfa: 1000, totalFcfa: 2000 },
    { productId: 'p-riz', productName: 'Riz en vrac', quantity: 1500, unitPriceFcfa: 800, totalFcfa: 1200, unit: 'Kilogram' },
  ],
  payments: [{ method: 'Cash', amountFcfa: 3000, changeFcfa: 0, reference: null }],
  cancellation: null, returns: [], ...overrides,
});

const done = (overrides: Partial<ReversalDone> = {}): ReversalDone => ({
  returnId: 'op', totalFcfa: 937, cashFcfa: 937, creditFcfa: 0, refund: [{ method: 'Cash', amountFcfa: 937 }], alreadyProcessed: false, ...overrides,
});

async function render(options: { sale?: SaleDetail; role?: string; online?: boolean; result?: ReversalDone; failure?: ApiError } = {}) {
  const api = {
    detail: vi.fn().mockResolvedValue(options.sale ?? detail()),
    returnLines: options.failure ? vi.fn().mockRejectedValue(options.failure) : vi.fn().mockResolvedValue(options.result ?? done()),
    cancel: vi.fn().mockResolvedValue(options.result ?? done({ totalFcfa: 3000, cashFcfa: 3000, refund: [{ method: 'Cash', amountFcfa: 3000 }] })),
    grant: vi.fn().mockResolvedValue({ token: 'sceau', approverId: 'm-1', approverName: 'Kofi Diaby', expiresAt: 'x' }),
  };
  const cash = { recordCashRefund: vi.fn().mockResolvedValue(undefined) };
  TestBed.configureTestingModule({
    providers: [
      provideRouter([]),
      { provide: ActivatedRoute, useValue: { snapshot: { paramMap: convertToParamMap({ id: 's-1' }) } } },
      { provide: SalesApi, useValue: api },
      { provide: CashService, useValue: cash },
      { provide: NetworkService, useValue: { online: signal(options.online ?? true) } },
      {
        provide: AuthService,
        useValue: {
          user: signal({ id: 'u-awa', displayName: 'Awa Koné', role: options.role ?? 'Manager' }),
          device: signal({ tenantId: 't', registerId: 'reg-1', pointOfSaleId: 'pos', name: 'Caisse 1', commerceName: 'Supérette' }),
        },
      },
    ],
  });
  const fixture = TestBed.createComponent(SaleDetailPage);
  const settle = async () => {
    await fixture.whenStable();
    await new Promise((resolve) => setTimeout(resolve, 5));
    await fixture.whenStable();
  };
  await settle();
  const root = fixture.nativeElement as HTMLElement;
  const text = () => (root.textContent ?? '').replace(/ /g, ' ');
  const click = async (label: string) => {
    const button = [...root.querySelectorAll('button')].find((candidate) => candidate.textContent?.includes(label));
    expect(button, `bouton « ${label} » introuvable`).toBeTruthy();
    button!.click();
    await settle();
  };
  const type = async (selector: string, value: string) => {
    const input = root.querySelector<HTMLInputElement>(selector)!;
    input.value = value;
    input.dispatchEvent(new Event('input'));
    await fixture.whenStable();
  };
  const choose = async (selector: string, value: string) => {
    const select = root.querySelector<HTMLSelectElement>(selector)!;
    select.value = value;
    select.dispatchEvent(new Event('change'));
    await fixture.whenStable();
  };
  const check = async (selector: string, checked: boolean) => {
    const box = root.querySelector<HTMLInputElement>(selector)!;
    box.checked = checked;
    box.dispatchEvent(new Event('change'));
    await settle();
  };
  return { api, cash, root, text, click, type, choose, check, settle };
}

describe('SaleDetailPage: lecture (ARR-09)', () => {
  it('shows the ticket, the lines with their units, the discount and the payment', async () => {
    const { text } = await render();

    expect(text()).toContain('Ticket n° 12');
    expect(text()).toContain("Jus d'orange 1L");
    expect(text()).toContain('1,500 kg');
    expect(text()).toContain('Remise : -200 FCFA');
    expect(text()).toContain('Espèces : 3 000 FCFA');
    expect(text()).toContain('Validée');
  });

  it('offers the return and the cancellation of a valid sale', async () => {
    const { text } = await render();
    expect(text()).toContain('Retour d’articles');
    expect(text()).toContain('Annuler la vente');
  });

  it('says a cancelled sale can do nothing more, and why it was cancelled', async () => {
    const { text } = await render({ sale: detail({ status: 'Cancelled', cancellation: { cancelledBy: 'm', reason: 'Erreur de saisie', at: '2026-10-26T09:00:00.000Z' } }) });

    expect(text()).toContain('Annulée');
    expect(text()).toContain('Erreur de saisie');
    expect(text()).not.toContain('Retour d’articles');
  });

  it('lists the returns already made and keeps the cancellation for a sale without any return', async () => {
    const { text, root } = await render({
      sale: detail({ returns: [{ id: 'r', kind: 'Return', at: '2026-10-26T09:00:00.000Z', by: 'u', approvedBy: null, reason: 'Client mécontent', totalFcfa: 937, cashFcfa: 937, lines: [{ lineIndex: 0, quantity: 1, resellable: true }], refund: [{ method: 'Cash', amountFcfa: 937 }] }] }),
    });

    expect(text()).toContain('Rendu : 937 FCFA');
    const cancel = [...root.querySelectorAll('button')].find((button) => button.textContent?.includes('Annuler la vente'));
    expect(cancel?.disabled).toBe(true);
    expect(text()).toContain('a déjà un retour');
  });

  it('needs the network and says so, without trying', async () => {
    const { text, api } = await render({ online: false });

    expect(text()).toContain('Cet écran a besoin du réseau');
    expect(api.detail).not.toHaveBeenCalled();
  });
});

describe('SaleDetailPage: retour (ARR-02, ARR-10)', () => {
  const startReturn = async (context: Awaited<ReturnType<typeof render>>) => {
    await context.click('Retour d’articles');
    await context.check('#retour-ligne-0', true);
    await context.type('#retour-quantite-0', '1');
    await context.choose('#motif', 'Client mécontent');
  };

  it('shows what the customer gets back before anything is sent', async () => {
    const context = await render();
    await startReturn(context);

    await context.click('Continuer');

    // 1 000 de jus moins sa part de remise (63) = 937.
    expect(context.text()).toContain('Vous rendez 937 FCFA en espèces');
    expect(context.api.returnLines).not.toHaveBeenCalled();
  });

  it('lets a manager confirm alone, then says how much to give back and notes the cash for the closing', async () => {
    const context = await render();
    await startReturn(context);
    await context.click('Continuer');

    await context.click('Confirmer le retour');

    expect(context.api.returnLines).toHaveBeenCalledWith('s-1', {
      operationId: expect.stringMatching(/^[0-9a-f-]{36}$/), registerId: 'reg-1', reason: 'Client mécontent',
      lines: [{ lineIndex: 0, quantity: 1, resellable: true }], approvalToken: null,
    });
    expect(context.text()).toContain('Rendez 937 FCFA en espèces');
    expect(context.cash.recordCashRefund).toHaveBeenCalledWith(expect.any(String), 937);
    expect(context.text()).toContain('Imprimer le ticket');
  });

  it('asks a cashier for the manager before sending, then sends with the signed approval', async () => {
    const context = await render({ role: 'Cashier' });
    await startReturn(context);
    await context.click('Continuer');
    expect(context.root.querySelector('#validation-pin')).toBeTruthy();
    expect(context.api.returnLines).not.toHaveBeenCalled();

    await context.type('#validation-identifiant', 'gerant');
    await context.type('#validation-pin', '482163');
    context.root.querySelector('#validation-pin')!.closest('form')!.dispatchEvent(new Event('submit', { cancelable: true }));
    await context.settle();

    expect(context.api.grant).toHaveBeenCalledWith({ identifier: 'gerant', pin: '482163', saleId: 's-1', purpose: 'reversal', amountFcfa: 0 });
    expect(context.api.returnLines.mock.calls[0][1].approvalToken).toBe('sceau');
    expect(context.text()).toContain('Validé par Kofi Diaby');
  });

  it('takes a weight with a comma for a weighed article, and refuses more than what was sold', async () => {
    const context = await render();
    await context.click('Retour d’articles');
    await context.check('#retour-ligne-1', true);
    await context.choose('#motif', 'Client mécontent');

    await context.type('#retour-quantite-1', '2');
    await context.click('Continuer');
    expect(context.text()).toContain('Vous rendez plus que ce qui a été vendu');

    await context.type('#retour-quantite-1', '0,5');
    await context.click('Continuer');
    expect(context.text()).toContain('Vous rendez 375 FCFA');
  });

  it('asks for a reason and for at least one article', async () => {
    const context = await render();
    await context.click('Retour d’articles');

    await context.click('Continuer');
    expect(context.text()).toContain('Cochez au moins un article');

    await context.check('#retour-ligne-0', true);
    await context.type('#retour-quantite-0', '1');
    await context.click('Continuer');
    expect(context.text()).toContain('Choisissez le motif');
  });

  it('sends damaged goods to the loss and keeps the shelf as the default', async () => {
    const context = await render();
    await startReturn(context);
    await context.choose('#retour-destination-0', 'loss');
    await context.click('Continuer');

    await context.click('Confirmer le retour');

    expect(context.api.returnLines.mock.calls[0][1].lines).toEqual([{ lineIndex: 0, quantity: 1, resellable: false }]);
  });

  it('tells what to do with mobile money and the debt of the customer', async () => {
    const context = await render({ result: done({ totalFcfa: 1500, cashFcfa: 0, creditFcfa: 500, refund: [{ method: 'Credit', amountFcfa: 500 }, { method: 'Wave', amountFcfa: 1000 }] }) });
    await startReturn(context);
    await context.click('Continuer');

    await context.click('Confirmer le retour');

    expect(context.text()).toContain('À rembourser par Wave : 1 000 FCFA');
    expect(context.text()).toContain('Dette effacée : 500 FCFA');
    expect(context.cash.recordCashRefund).not.toHaveBeenCalled();
  });

  it('shows the message of the server and keeps the same operation for a new try, so it never refunds twice', async () => {
    const context = await render({ failure: new ApiError(422, 'rule_violation', 'Ouvrez la caisse pour rembourser en espèces.') });
    await startReturn(context);
    await context.click('Continuer');

    await context.click('Confirmer le retour');
    expect(context.text()).toContain('Ouvrez la caisse pour rembourser en espèces.');
    await context.click('Confirmer le retour');

    const [first, second] = context.api.returnLines.mock.calls.map((call) => call[1].operationId);
    expect(second).toBe(first);
  });
});

describe('SaleDetailPage: annulation (ARR-01, ARR-10)', () => {
  it('cancels the whole sale after a reason, with a clear summary and the choice for the goods', async () => {
    const context = await render();
    await context.click('Annuler la vente');
    await context.choose('#motif', 'Erreur de saisie');
    await context.check('#marchandise-abimee', true);

    await context.click('Continuer');
    expect(context.text()).toContain('Vous rendez 3 000 FCFA en espèces');
    await context.click('Annuler la vente');

    expect(context.api.cancel).toHaveBeenCalledWith('s-1', {
      operationId: expect.stringMatching(/^[0-9a-f-]{36}$/), registerId: 'reg-1', reason: 'Erreur de saisie', resellable: false, approvalToken: null,
    });
    expect(context.text()).toContain('Rendez 3 000 FCFA en espèces');
    expect(context.cash.recordCashRefund).toHaveBeenCalledWith(expect.any(String), 3000);
  });

  it('takes a reason of its own when none of the usual ones fits', async () => {
    const context = await render();
    await context.click('Annuler la vente');
    await context.choose('#motif', 'Autre');
    await context.type('#motif-libre', 'Client parti sans payer');

    await context.click('Continuer');
    await context.click('Annuler la vente');

    expect(context.api.cancel.mock.calls[0][1].reason).toBe('Client parti sans payer');
  });

  it('asks a cashier for a manager first', async () => {
    const context = await render({ role: 'Cashier' });
    await context.click('Annuler la vente');
    await context.choose('#motif', 'Erreur de saisie');

    await context.click('Continuer');

    expect(context.root.querySelector('#validation-pin')).toBeTruthy();
    expect(context.api.cancel).not.toHaveBeenCalled();
  });
});
