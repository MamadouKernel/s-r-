import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { CashMovementOperation } from '../data/journal';
import { AuthService } from '../services/auth.service';
import { CashService } from '../services/cash.service';
import { NetworkService } from '../services/network.service';
import { SalesApi } from '../sales/sales-api';
import { MovementsPage } from './movements-page';

const movement = (overrides: Partial<CashMovementOperation> = {}): CashMovementOperation => ({
  kind: 'cashMovement', seq: 3, status: 'pending', createdAt: '2026-10-28T09:00:00.000Z', movementId: 'm-0', sessionId: 's', movementKind: 'Expense',
  amountFcfa: 1500, reason: 'Transport', at: '2026-10-28T09:00:00.000Z', ...overrides,
});

async function render(options: { role?: string; online?: boolean; list?: CashMovementOperation[]; failure?: string } = {}) {
  const list = signal(options.list ?? []);
  const recordMovement = vi.fn().mockImplementation(async (input) => {
    if (options.failure) {
      throw new Error(options.failure);
    }
    list.update((items) => [...items, movement({ seq: 9, movementId: 'new', movementKind: input.kind, amountFcfa: input.amountFcfa, reason: input.reason })]);
  });
  const grant = vi.fn().mockResolvedValue({ token: 'sceau', approverId: 'm', approverName: 'Kofi Diaby', expiresAt: 'x' });
  TestBed.configureTestingModule({
    providers: [
      provideRouter([]),
      { provide: CashService, useValue: { movements: async () => list(), recordMovement, movementId: () => 'mv-1' } },
      { provide: NetworkService, useValue: { online: signal(options.online ?? true) } },
      { provide: AuthService, useValue: { user: signal({ id: 'u', displayName: 'Awa', role: options.role ?? 'Cashier' }) } },
      { provide: SalesApi, useValue: { grant } },
    ],
  });
  const fixture = TestBed.createComponent(MovementsPage);
  const settle = async () => {
    await fixture.whenStable();
    await new Promise((resolve) => setTimeout(resolve, 5));
    await fixture.whenStable();
  };
  await settle();
  const root = fixture.nativeElement as HTMLElement;
  const text = () => (root.textContent ?? '').replace(/ /g, ' ');
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
    await settle();
  };
  const submit = async () => {
    root.querySelector('form')!.dispatchEvent(new Event('submit', { cancelable: true }));
    await settle();
  };
  const click = async (label: string) => {
    [...root.querySelectorAll('button')].find((button) => button.textContent?.includes(label))!.click();
    await settle();
  };
  return { root, text, type, choose, submit, click, recordMovement, grant, settle };
}

describe('MovementsPage: sorties et entrées d’espèces (CLO-10)', () => {
  it('records an expense with its reason from the usual list', async () => {
    const { text, choose, type, submit, recordMovement } = await render();
    await type('#montant', '1500');
    await choose('#motif', 'Transport');

    await submit();

    expect(recordMovement).toHaveBeenCalledWith({ kind: 'Expense', amountFcfa: 1500, reason: 'Transport' });
    expect(text()).toContain('Dépense · 1 500 FCFA · Transport');
  });

  it('takes a reason of its own for « Autre » and asks for one on an expense', async () => {
    const { text, choose, type, submit, recordMovement } = await render();
    await type('#montant', '800');

    await submit();
    expect(text()).toContain('Dites à quoi sert cette dépense.');
    expect(recordMovement).not.toHaveBeenCalled();

    await choose('#motif', 'Autre');
    await type('#motif-libre', 'Ficelle pour les colis');
    await submit();
    expect(recordMovement).toHaveBeenCalledWith({ kind: 'Expense', amountFcfa: 800, reason: 'Ficelle pour les colis' });
  });

  it('records a drop and a topup without any reason', async () => {
    const { choose, type, submit, recordMovement } = await render();
    await choose('#type', 'Drop');
    await type('#montant', '4000');
    await submit();
    expect(recordMovement).toHaveBeenLastCalledWith({ kind: 'Drop', amountFcfa: 4000, reason: null });

    await choose('#type', 'Topup');
    await type('#montant', '2000');
    await submit();
    expect(recordMovement).toHaveBeenLastCalledWith({ kind: 'Topup', amountFcfa: 2000, reason: null });
  });

  it('refuses an amount that is not a whole number of francs', async () => {
    const { text, type, choose, submit, recordMovement } = await render();
    await choose('#motif', 'Repas');
    await type('#montant', '12,5');

    await submit();

    expect(text()).toContain('Saisissez le montant en francs, en chiffres entiers.');
    expect(recordMovement).not.toHaveBeenCalled();
  });

  it('asks a cashier for a manager above 5 000 FCFA, then records with the signed approval', async () => {
    const { root, text, type, choose, submit, recordMovement, grant, settle } = await render();
    await choose('#motif', 'Transport');
    await type('#montant', '8000');

    await submit();

    expect(recordMovement).not.toHaveBeenCalled();
    expect(root.querySelector('#validation-pin')).toBeTruthy();
    await type('#validation-identifiant', 'gerant');
    await type('#validation-pin', '482163');
    root.querySelector('#validation-pin')!.closest('form')!.dispatchEvent(new Event('submit', { cancelable: true }));
    await settle();

    expect(grant).toHaveBeenCalledWith({ identifier: 'gerant', pin: '482163', saleId: 'mv-1', purpose: 'cash-out', amountFcfa: 8000 });
    expect(recordMovement).toHaveBeenCalledWith({ kind: 'Expense', amountFcfa: 8000, reason: 'Transport', approvalToken: 'sceau' });
    expect(text()).toContain('Dépense · 8 000 FCFA');
  });

  it('lets a manager record a big drop alone, and a cashier a topup of any size', async () => {
    const manager = await render({ role: 'Manager' });
    await manager.choose('#type', 'Drop');
    await manager.type('#montant', '40000');
    await manager.submit();
    expect(manager.recordMovement).toHaveBeenCalledWith({ kind: 'Drop', amountFcfa: 40000, reason: null });

    TestBed.resetTestingModule();
    const cashier = await render();
    await cashier.choose('#type', 'Topup');
    await cashier.type('#montant', '30000');
    await cashier.submit();
    expect(cashier.recordMovement).toHaveBeenCalledWith({ kind: 'Topup', amountFcfa: 30000, reason: null });
  });

  it('says a big exit needs the network and does not try without it', async () => {
    const { text, root, type, choose, submit, recordMovement } = await render({ online: false });
    await choose('#motif', 'Transport');
    await type('#montant', '8000');

    await submit();

    expect(text()).toContain('Il faut du réseau pour qu’un gérant valide');
    expect(root.querySelector('#validation-pin')).toBeNull();
    expect(recordMovement).not.toHaveBeenCalled();
  });

  it('shows the message of the journal when it refuses, such as an exit bigger than the drawer', async () => {
    const { text, choose, type, submit } = await render({ failure: 'Cette sortie dépasse l’argent qui doit se trouver dans le tiroir.' });
    await choose('#motif', 'Transport');
    await type('#montant', '900');

    await submit();

    expect(text()).toContain('Cette sortie dépasse l’argent qui doit se trouver dans le tiroir.');
  });

  it('corrects a mistake with an opposite movement that needs its own reason, never by deleting', async () => {
    const { text, click, type, recordMovement, settle } = await render({ list: [movement()] });

    await click('Annuler ce mouvement');
    await click('Confirmer l’annulation');
    expect(text()).toContain('Dites pourquoi vous annulez ce mouvement.');
    expect(recordMovement).not.toHaveBeenCalled();

    await type('#annulation-motif', 'Saisie en double');
    await click('Confirmer l’annulation');
    await settle();

    expect(recordMovement).toHaveBeenCalledWith({ kind: 'Topup', amountFcfa: 1500, reason: 'Annulation de « Transport » : Saisie en double' });
  });

  it('lists the movements of the session with their state and shows a refused one with its reason', async () => {
    const { text } = await render({
      list: [movement(), movement({ seq: 4, movementId: 'm-1', status: 'rejected', message: 'Au-delà de 5 000 FCFA, l’accord d’un gérant est nécessaire.', amountFcfa: 9000 })],
    });

    expect(text()).toContain('Dépense · 1 500 FCFA · Transport');
    expect(text()).toContain('En attente');
    expect(text()).toContain('Refusée');
    expect(text()).toContain('l’accord d’un gérant est nécessaire');
  });
});
