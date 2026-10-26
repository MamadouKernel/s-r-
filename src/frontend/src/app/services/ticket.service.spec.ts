import { TestBed } from '@angular/core/testing';
import { Receipt } from '../core/receipt';
import { TicketService } from './ticket.service';

const receipt: Receipt = {
  commerce: 'Supérette du Carrefour', at: new Date(2026, 9, 25, 14, 32).toISOString(), cashier: 'Awa Koné', register: 'Caisse 1', rank: 12,
  lines: [{ name: 'Pain de mie', quantity: 1, unitPriceFcfa: 500, totalFcfa: 500 }], discountFcfa: 0, totalFcfa: 500,
  payments: [{ label: 'Espèces', amountFcfa: 500 }], tenderedFcfa: 500, changeFcfa: 0, customer: null,
};

function service(): TicketService {
  TestBed.resetTestingModule();
  return TestBed.inject(TicketService);
}

describe('TicketService', () => {
  beforeEach(() => localStorage.clear());
  afterEach(() => {
    vi.unstubAllGlobals();
    document.querySelectorAll('iframe').forEach((frame) => frame.remove());
  });

  it('prints on 58 mm paper by default and remembers the choice of the user', () => {
    expect(service().width()).toBe(58);

    const tickets = service();
    tickets.setWidth(80);
    expect(tickets.width()).toBe(80);
    expect(localStorage.getItem('soro.ticketWidth')).toBe('80');
    expect(service().width()).toBe(80);
  });

  it('ignores a width it does not know', () => {
    localStorage.setItem('soro.ticketWidth', '99');
    expect(service().width()).toBe(58);
  });

  it('prints the ticket through a hidden page, and takes it away afterwards', () => {
    const tickets = service();
    tickets.setWidth(58);
    const print = vi.fn();

    tickets.print(receipt);
    const frame = document.querySelector<HTMLIFrameElement>('iframe')!;
    expect(frame).toBeTruthy();
    expect(frame.srcdoc).toContain('Supérette du Carrefour');
    expect(frame.srcdoc).toContain('width: 48mm');
    expect(frame.getAttribute('aria-hidden')).toBe('true');

    frame.contentWindow!.print = print;
    frame.dispatchEvent(new Event('load'));
    expect(print).toHaveBeenCalledOnce();

    frame.contentWindow!.dispatchEvent(new Event('afterprint'));
    expect(document.querySelector('iframe')).toBeNull();
  });

  it('shares the ticket as text in the width of the paper, and says whether the device can share', async () => {
    const share = vi.fn().mockResolvedValue(undefined);
    vi.stubGlobal('navigator', { ...navigator, share });
    const tickets = service();

    expect(tickets.canShare).toBe(true);
    expect(await tickets.share(receipt)).toBe(true);

    const shared = share.mock.calls[0][0] as { title: string; text: string };
    expect(shared.title).toBe('Ticket de caisse');
    expect(shared.text).toContain('Pain de mie');
    expect(Math.max(...shared.text.split('\n').map((line) => line.length))).toBeLessThanOrEqual(32);
  });

  it('does not complain when the user closes the sharing menu', async () => {
    vi.stubGlobal('navigator', { ...navigator, share: vi.fn().mockRejectedValue(new DOMException('annulé', 'AbortError')) });

    expect(await service().share(receipt)).toBe(false);
  });

  it('knows when the device cannot share', () => {
    vi.stubGlobal('navigator', { ...navigator, share: undefined });
    expect(service().canShare).toBe(false);
  });

  it('copies the same text to the clipboard, and says when it cannot', async () => {
    const writeText = vi.fn().mockResolvedValue(undefined);
    vi.stubGlobal('navigator', { ...navigator, clipboard: { writeText } });
    const tickets = service();
    tickets.setWidth(80);

    expect(await tickets.copy(receipt)).toBe(true);
    expect(writeText.mock.calls[0][0]).toContain('Pain de mie');

    vi.stubGlobal('navigator', { ...navigator, clipboard: { writeText: vi.fn().mockRejectedValue(new Error('refusé')) } });
    expect(await tickets.copy(receipt)).toBe(false);
  });
});
