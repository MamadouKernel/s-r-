import 'fake-indexeddb/auto';
import { TestBed } from '@angular/core/testing';
import { signal } from '@angular/core';
import { Operation } from '../data/journal';
import { JournalService } from '../services/journal.service';
import { SyncService } from '../services/sync.service';
import { QueuePage } from './queue-page';

async function render(operations: Operation[]) {
  TestBed.configureTestingModule({
    providers: [
      { provide: JournalService, useValue: { get: () => Promise.resolve({ operations: async () => operations }) } },
      { provide: SyncService, useValue: { counts: signal({ pending: 0, synced: 0, rejected: 0 }), state: signal('idle'), requestFlush: vi.fn() } },
    ],
  });
  const fixture = TestBed.createComponent(QueuePage);
  await fixture.whenStable();
  await new Promise((resolve) => setTimeout(resolve, 5));
  await fixture.whenStable();
  return ((fixture.nativeElement as HTMLElement).textContent ?? '').replace(/ /g, ' ');
}

describe('QueuePage: mouvements et clôture à l’aveugle', () => {
  it('names a cash movement in plain words with its amount and reason', async () => {
    const text = await render([
      { kind: 'cashMovement', seq: 3, status: 'pending', createdAt: 'x', movementId: 'm', sessionId: 's', movementKind: 'Expense', amountFcfa: 1500, reason: 'Transport', at: 'x' },
      { kind: 'cashMovement', seq: 4, status: 'synced', createdAt: 'x', movementId: 'd', sessionId: 's', movementKind: 'Drop', amountFcfa: 40000, reason: null, at: 'x' },
      { kind: 'cashMovement', seq: 5, status: 'synced', createdAt: 'x', movementId: 't', sessionId: 's', movementKind: 'Topup', amountFcfa: 2000, reason: null, at: 'x' },
    ]);

    expect(text).toContain('Dépense · 1 500 FCFA · Transport');
    expect(text).toContain('Versement au coffre · 40 000 FCFA');
    expect(text).toContain('Apport de monnaie · 2 000 FCFA');
  });

  it('never shows the variance of a closing, even when the server gave it: the cashier counts blind', async () => {
    const text = await render([
      {
        kind: 'closeSession', seq: 9, status: 'synced', createdAt: 'x', sessionId: 's', countedCashFcfa: 9800, at: 'x',
        result: { closed: true, needsRecount: false, countedCashFcfa: 9800, expectedCashFcfa: 10_000, varianceFcfa: -200 },
      },
    ]);

    expect(text).toContain('Clôture · compté 9 800 FCFA');
    expect(text).not.toContain('écart');
    expect(text).not.toContain('10 000');
  });
});
