import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { AuthService } from '../services/auth.service';
import { NetworkService } from '../services/network.service';
import { TeamApi } from '../management/team-api';
import { ApiError } from '../sync/sync-api';
import { SaleSummary, SalesApi } from './sales-api';
import { SalesPage } from './sales-page';

const sale = (overrides: Partial<SaleSummary> = {}): SaleSummary => ({
  id: 's-1', ticket: 12, recordedAt: '2026-10-26T08:05:00.000Z', cashierId: 'u-awa', registerId: 'r', sessionId: 'sess', netTotalFcfa: 2000, discountFcfa: 0,
  status: 'Validated', paymentMethods: ['Cash'], lineCount: 2, returnedFcfa: 0, ...overrides,
});

async function render(options: { sales?: SaleSummary[]; role?: string; online?: boolean; failure?: ApiError } = {}) {
  const list = options.failure ? vi.fn().mockRejectedValue(options.failure) : vi.fn().mockResolvedValue(options.sales ?? [sale()]);
  TestBed.configureTestingModule({
    providers: [
      provideRouter([]),
      { provide: SalesApi, useValue: { list } },
      { provide: NetworkService, useValue: { online: signal(options.online ?? true) } },
      { provide: AuthService, useValue: { user: signal({ id: 'u-awa', displayName: 'Awa Koné', role: options.role ?? 'Cashier' }) } },
      { provide: TeamApi, useValue: { directory: vi.fn().mockResolvedValue([{ id: 'u-awa', displayName: 'Awa Koné' }, { id: 'u-kofi', displayName: 'Kofi Diaby' }]) } },
    ],
  });
  const fixture = TestBed.createComponent(SalesPage);
  await fixture.whenStable();
  await new Promise((resolve) => setTimeout(resolve, 5));
  await fixture.whenStable();
  const root = fixture.nativeElement as HTMLElement;
  return { root, list, fixture, text: () => (root.textContent ?? '').replace(/ /g, ' ') };
}

describe('SalesPage: Ventes du jour (ARR-09)', () => {
  it('lists the recent sales with their ticket number, time, total and state, each one opening its detail', async () => {
    const { root, text } = await render({
      sales: [
        sale(),
        sale({ id: 's-2', ticket: 13, status: 'Cancelled', netTotalFcfa: 500 }),
        sale({ id: 's-3', ticket: 14, returnedFcfa: 600, netTotalFcfa: 1800 }),
      ],
    });

    expect(text()).toContain('Ticket n° 12');
    expect(text()).toContain('2 000 FCFA');
    expect(text()).toContain('Annulée');
    expect(text()).toContain('Retour partiel : 600 FCFA rendus');
    const links = [...root.querySelectorAll('a')].map((link) => link.getAttribute('href'));
    expect(links).toContain('/ventes/s-1');
    expect(links).toContain('/ventes/s-3');
  });

  it('finds a ticket by its number', async () => {
    const { root, fixture, text } = await render({ sales: [sale(), sale({ id: 's-2', ticket: 13 })] });
    const search = root.querySelector<HTMLInputElement>('#recherche-ticket')!;

    search.value = '13';
    search.dispatchEvent(new Event('input'));
    await fixture.whenStable();

    expect(text()).toContain('Ticket n° 13');
    expect(text()).not.toContain('Ticket n° 12');
  });

  it('says so when nothing matches or nothing was sold', async () => {
    const empty = await render({ sales: [] });
    expect(empty.text()).toContain('Aucune vente');
  });

  it('shows a manager the name of the cashier of each sale, and a cashier only his own sales without names', async () => {
    const manager = await render({ role: 'Manager', sales: [sale({ cashierId: 'u-kofi' })] });
    expect(manager.text()).toContain('Kofi Diaby');

    TestBed.resetTestingModule();
    const cashier = await render({ sales: [sale()] });
    expect(cashier.text()).not.toContain('Awa Koné ·');
  });

  it('needs the network and says it, without calling the server', async () => {
    const { text, list } = await render({ online: false });

    expect(text()).toContain('Cet écran a besoin du réseau');
    expect(list).not.toHaveBeenCalled();
  });

  it('says what went wrong and lets the cashier try again', async () => {
    const { root, text, list, fixture } = await render({ failure: new ApiError(0, 'network', 'x') });
    expect(text()).toContain('Le serveur ne répond pas');

    list.mockResolvedValue([sale()]);
    [...root.querySelectorAll('button')].find((button) => button.textContent?.includes('Réessayer'))!.click();
    await fixture.whenStable();
    await new Promise((resolve) => setTimeout(resolve, 5));
    await fixture.whenStable();

    expect(text()).toContain('Ticket n° 12');
  });

  it('keeps only the last seven days', async () => {
    const { text } = await render({ sales: [sale({ recordedAt: new Date(Date.now() - 2 * 86_400_000).toISOString() }), sale({ id: 'old', ticket: 3, recordedAt: new Date(Date.now() - 10 * 86_400_000).toISOString() })] });

    expect(text()).toContain('Ticket n° 12');
    expect(text()).not.toContain('Ticket n° 3 ');
  });
});
