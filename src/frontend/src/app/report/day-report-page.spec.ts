import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { NetworkService } from '../services/network.service';
import { ApiError } from '../sync/sync-api';
import { DayReport, DayReportApi } from './day-report-api';
import { DayReportPage } from './day-report-page';

const report = (overrides: Partial<DayReport> = {}): DayReport => ({
  day: '2026-10-28', salesCount: 4, netTotalFcfa: 20_000, averageBasketFcfa: 5_000,
  payments: [{ method: 'Cash', amountFcfa: 12_000, count: 3 }, { method: 'Wave', amountFcfa: 8_000, count: 1 }],
  cashiers: [{ cashierId: 'u-awa', salesCount: 4, netTotalFcfa: 20_000, discountsCount: 1, discountFcfa: 500, cancellationsCount: 1, cancelledFcfa: 2_000, returnsCount: 1, returnedFcfa: 1_000, varianceFcfa: -2_000, varianceSessions: 1 }],
  movements: [{ kind: 'Expense', reason: 'Transport', count: 2, amountFcfa: 3_000 }, { kind: 'Drop', reason: null, count: 1, amountFcfa: 40_000 }],
  sessions: [
    { id: 's-1', cashierId: 'u-kofi', openedAt: '2026-10-27T08:00:00.000Z', closedAt: null, status: 'Open', openingFloatFcfa: 10_000, expectedCashFcfa: 120_000, countedCashFcfa: null, varianceFcfa: null, needsClosing: true, drawerAboveWarning: true },
    { id: 's-2', cashierId: 'u-awa', openedAt: '2026-10-28T08:00:00.000Z', closedAt: '2026-10-28T17:00:00.000Z', status: 'Closed', openingFloatFcfa: 10_000, expectedCashFcfa: 12_000, countedCashFcfa: 10_000, varianceFcfa: -2_000, needsClosing: false, drawerAboveWarning: false },
  ],
  debtCreatedFcfa: 1_500, repaymentsFcfa: 700,
  lowStock: [{ name: "Jus d'orange 1L", quantity: 2, threshold: 5, unit: 'Piece' }],
  names: { 'u-awa': 'Awa Koné', 'u-kofi': 'Kofi Diaby' },
  ...overrides,
});

async function render(options: { data?: DayReport; online?: boolean; failure?: ApiError } = {}) {
  const day = options.failure ? vi.fn().mockRejectedValue(options.failure) : vi.fn().mockResolvedValue(options.data ?? report());
  TestBed.configureTestingModule({
    providers: [
      { provide: DayReportApi, useValue: { day } },
      { provide: NetworkService, useValue: { online: signal(options.online ?? true) } },
    ],
  });
  const fixture = TestBed.createComponent(DayReportPage);
  const settle = async () => {
    await fixture.whenStable();
    await new Promise((resolve) => setTimeout(resolve, 5));
    await fixture.whenStable();
  };
  await settle();
  const root = fixture.nativeElement as HTMLElement;
  return { root, day, settle, fixture, text: () => (root.textContent ?? '').replace(/ /g, ' ') };
}

describe('DayReportPage: Rapport du jour (CLO-11)', () => {
  it('reads the day of today and shows the sales, the payments with the Mobile Money to find on the phone, and the average basket', async () => {
    const { text, day } = await render();

    expect(day).toHaveBeenCalledWith(new Date().toISOString().slice(0, 10));
    expect(text()).toContain('20 000 FCFA');
    expect(text()).toContain('4 tickets');
    expect(text()).toContain('Panier moyen : 5 000 FCFA');
    expect(text()).toContain('Espèces : 12 000 FCFA');
    expect(text()).toContain('À retrouver sur votre téléphone');
    expect(text()).toContain('Wave : 8 000 FCFA');
  });

  it('shows each cashier with the sales, the discounts, the cancellations, the returns and the cash gap in plain words, never a deduction', async () => {
    const { text } = await render();

    expect(text()).toContain('Awa Koné');
    expect(text()).toContain('remises 1 (500 FCFA)');
    expect(text()).toContain('annulations 1 (2 000 FCFA)');
    expect(text()).toContain('retours 1 (1 000 FCFA)');
    expect(text()).toContain('écart de caisse : manque de 2 000 FCFA');
    expect(text()).not.toMatch(/retenue|variance|session/i);
  });

  it('puts the cash still open first and says it must be closed, and warns about a drawer with a lot of money', async () => {
    const { text } = await render();

    expect(text()).toContain('À clôturer');
    expect(text()).toContain('La caisse de Kofi Diaby est ouverte depuis le 27/10');
    expect(text()).toContain('faites un versement au coffre');
    expect(text()).toContain('clôturée à');
  });

  it('lists the outflows with their reasons, the credit and the stock to watch', async () => {
    const { text } = await render();

    expect(text()).toContain('Dépenses (Transport) : 3 000 FCFA en 2 fois');
    expect(text()).toContain('Versements au coffre : 40 000 FCFA');
    expect(text()).toContain('dettes créées 1 500 FCFA');
    expect(text()).toContain("Jus d'orange 1L : 2 (seuil 5)");
  });

  it('changes day with the date field and reads that day', async () => {
    const { root, day, settle } = await render();
    const field = root.querySelector<HTMLInputElement>('#jour')!;

    field.value = '2026-10-20';
    field.dispatchEvent(new Event('change'));
    await settle();

    expect(day).toHaveBeenLastCalledWith('2026-10-20');
  });

  it('says plainly when nothing happened that day', async () => {
    const { text } = await render({ data: report({ salesCount: 0, netTotalFcfa: 0, averageBasketFcfa: 0, payments: [], cashiers: [], movements: [], sessions: [], debtCreatedFcfa: 0, repaymentsFcfa: 0, lowStock: [] }) });

    expect(text()).toContain('Aucune vente ce jour-là');
  });

  it('needs the network and says so, and says what went wrong with a way to try again', async () => {
    const offline = await render({ online: false });
    expect(offline.text()).toContain('Cet écran a besoin du réseau');
    expect(offline.day).not.toHaveBeenCalled();

    TestBed.resetTestingModule();
    const failed = await render({ failure: new ApiError(0, 'network', 'x') });
    expect(failed.text()).toContain('Le serveur ne répond pas');
    expect(failed.text()).toContain('Réessayer');
  });
});
