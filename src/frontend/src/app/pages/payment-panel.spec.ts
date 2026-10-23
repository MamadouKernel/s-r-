import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { SaleOperation } from '../data/journal';
import { PaymentDraft } from '../domain/types';
import { referenceKey } from '../domain/payment';
import { CashService } from '../services/cash.service';
import { SyncService } from '../services/sync.service';
import { PaymentPanel } from './payment-panel';

const sale = (draft: PaymentDraft[]): SaleOperation => ({
  kind: 'sale', seq: 2, status: 'pending', createdAt: 'x', saleId: 's', sessionId: 'sess', at: 'x', discountFcfa: 0,
  netTotalFcfa: 1450, changeFcfa: 0, lines: [], payments: draft,
});

async function render(used: string[] = [], pending = 1) {
  const confirmSale = vi.fn(async (drafts: PaymentDraft[]) => sale(drafts));
  TestBed.configureTestingModule({
    providers: [
      { provide: SyncService, useValue: { counts: signal({ pending, synced: 0, rejected: 0 }) } },
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
  return { fixture, root, confirmSale, type, click };
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
