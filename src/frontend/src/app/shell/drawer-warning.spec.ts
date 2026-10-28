import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { CashService } from '../services/cash.service';
import { SyncService } from '../services/sync.service';
import { DrawerWarning } from './drawer-warning';

async function render(options: { open?: boolean; expected?: number }) {
  TestBed.configureTestingModule({
    providers: [
      provideRouter([]),
      { provide: CashService, useValue: { session: signal(options.open === false ? null : { sessionId: 's' }), summary: async () => ({ expectedCashFcfa: options.expected ?? 0 }) } },
      { provide: SyncService, useValue: { counts: signal({ pending: 0, synced: 0, rejected: 0 }) } },
    ],
  });
  const fixture = TestBed.createComponent(DrawerWarning);
  await fixture.whenStable();
  await new Promise((resolve) => setTimeout(resolve, 5));
  await fixture.whenStable();
  return fixture.nativeElement as HTMLElement;
}

describe('DrawerWarning (CLO-13)', () => {
  it('warns that there is a lot of money in the drawer and points to the movements, without a number', async () => {
    const root = await render({ expected: 120_000 });

    expect(root.textContent).toContain('Il y a beaucoup d’argent dans le tiroir : faites un versement au coffre.');
    expect(root.textContent).not.toContain('120');
    expect(root.querySelector('a')?.getAttribute('href')).toBe('/mouvements');
  });

  it('says nothing at or under the threshold, nor without an open session', async () => {
    expect((await render({ expected: 100_000 })).textContent?.trim()).toBe('');
    TestBed.resetTestingModule();
    expect((await render({ open: false, expected: 500_000 })).textContent?.trim()).toBe('');
  });
});
