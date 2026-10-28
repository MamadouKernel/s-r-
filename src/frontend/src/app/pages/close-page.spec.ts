import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { AuthService } from '../services/auth.service';
import { CashService } from '../services/cash.service';
import { SyncService } from '../services/sync.service';
import { ClosePage } from './close-page';

const summary = (expectedCashFcfa = 10_000) => ({ salesCount: 3, netTotalFcfa: 5000, expectedCashFcfa, refundsFcfa: 600, paidOutFcfa: 1500, paidInFcfa: 0 });

async function render(options: { role?: string; expected?: number } = {}) {
  const closeSession = vi.fn().mockImplementation(async (counted: number) => ({ kind: 'closeSession', seq: 9, status: 'pending', createdAt: 'x', sessionId: 's', countedCashFcfa: counted, at: 'x' }));
  TestBed.configureTestingModule({
    providers: [
      provideRouter([]),
      { provide: CashService, useValue: { summary: async () => summary(options.expected), held: signal([]), closeSession } },
      { provide: SyncService, useValue: { counts: signal({ pending: 1, synced: 0, rejected: 0 }) } },
      { provide: AuthService, useValue: { user: signal({ id: 'u', displayName: 'Awa', role: options.role ?? 'Cashier' }) } },
    ],
  });
  const fixture = TestBed.createComponent(ClosePage);
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
  const submit = async () => {
    root.querySelector('form')!.dispatchEvent(new Event('submit', { cancelable: true }));
    await settle();
  };
  const click = async (label: string) => {
    [...root.querySelectorAll('button')].find((button) => button.textContent?.includes(label))!.click();
    await settle();
  };
  return { root, text, type, submit, click, closeSession, settle };
}

describe('ClosePage: clôture à l’aveugle (CLO-09)', () => {
  it('asks the cashier to count and shows neither what is expected, nor the sales, nor the refunds, nor the outflows', async () => {
    const { text } = await render();

    expect(text()).toContain('Comptez les espèces du tiroir');
    for (const hidden of ['attendu', 'attendue', '10 000', 'Remboursements', 'Total des ventes', '1 500', 'écart']) {
      expect(text()).not.toContain(hidden);
    }
  });

  it('closes when the count is within the tolerance and says only what was counted', async () => {
    const { text, type, submit, closeSession } = await render();
    await type('#compte', '9800');

    await submit();

    expect(closeSession).toHaveBeenCalledWith(9800, { recount: false, denominations: undefined });
    expect(text()).toContain('Caisse clôturée');
    expect(text()).toContain('Montant compté : 9 800 FCFA');
    expect(text()).not.toContain('attendu');
    expect(text()).not.toContain('écart');
  });

  it('asks for one recount beyond the tolerance, without a word on the amount, then closes on the second count', async () => {
    const { text, root, type, submit, closeSession } = await render();
    await type('#compte', '12000');

    await submit();

    expect(closeSession).not.toHaveBeenCalled();
    expect(text()).toContain('Votre comptage ne correspond pas à ce qui est attendu. Recomptez, billet par billet, puis confirmez.');
    expect(text()).not.toMatch(/12 000|manque|excédent|10 000/);
    expect(root.querySelector<HTMLInputElement>('#compte')!.value).toBe('');

    await type('#compte', '8000');
    await submit();

    expect(closeSession).toHaveBeenCalledTimes(1);
    expect(closeSession).toHaveBeenCalledWith(8000, { recount: true, denominations: undefined });
    expect(text()).toContain('Caisse clôturée');
  });

  it('adds up the notes and coins counted one by one, fills the amount and sends the detail with the count', async () => {
    const { text, root, click, type, submit, closeSession } = await render({ expected: 9_000 });
    await click('Compter par billets');

    await type('#coupure-5000', '1');
    await type('#coupure-1000', '3');
    await type('#coupure-500', '2');

    expect(text()).toContain('Total : 9 000 FCFA');
    expect(root.querySelector<HTMLInputElement>('#compte')!.value).toBe('9000');
    await submit();
    expect(closeSession).toHaveBeenCalledWith(9000, { recount: false, denominations: { 5000: 1, 1000: 3, 500: 2 } });
  });

  it('refuses an amount that is not a whole number of francs', async () => {
    const { text, type, submit, closeSession } = await render();
    await type('#compte', '12,5');

    await submit();

    expect(text()).toContain('Saisissez le montant compté en chiffres, sans virgule.');
    expect(closeSession).not.toHaveBeenCalled();
  });

  it('shows a manager the expected amount and the variance once he has closed, and a cashier never', async () => {
    const manager = await render({ role: 'Manager' });
    expect(manager.text()).not.toContain('attendu');
    await manager.type('#compte', '9900');
    await manager.submit();
    expect(manager.text()).toContain('Montant attendu : 10 000 FCFA');
    expect(manager.text()).toContain('Manque de 100 FCFA');
  });
});
