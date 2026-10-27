import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { CashService } from '../services/cash.service';
import { SyncService } from '../services/sync.service';
import { ClosePage } from './close-page';

async function render(summary: { salesCount: number; netTotalFcfa: number; expectedCashFcfa: number; refundsFcfa: number }) {
  TestBed.configureTestingModule({
    providers: [
      provideRouter([]),
      { provide: CashService, useValue: { summary: async () => summary, held: signal([]), closeSession: vi.fn() } },
      { provide: SyncService, useValue: { counts: signal({ pending: 0, synced: 0, rejected: 0 }) } },
    ],
  });
  const fixture = TestBed.createComponent(ClosePage);
  await fixture.whenStable();
  await new Promise((resolve) => setTimeout(resolve, 5));
  await fixture.whenStable();
  return ((fixture.nativeElement as HTMLElement).textContent ?? '').replace(/ /g, ' ');
}

describe('ClosePage: remboursements (ARR-05)', () => {
  it('shows the cash given back to customers and the cash expected without it', async () => {
    const text = await render({ salesCount: 3, netTotalFcfa: 5000, expectedCashFcfa: 14_400, refundsFcfa: 600 });

    expect(text).toContain('Remboursements en espèces');
    expect(text).toContain('-600 FCFA');
    expect(text).toContain('14 400 FCFA');
  });

  it('shows no refund line when nothing was given back', async () => {
    const text = await render({ salesCount: 3, netTotalFcfa: 5000, expectedCashFcfa: 15_000, refundsFcfa: 0 });

    expect(text).not.toContain('Remboursements en espèces');
  });
});
