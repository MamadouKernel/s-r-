import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Receipt } from '../core/receipt';
import { TicketService } from '../services/ticket.service';
import { TicketActions } from './ticket-actions';

const receipt = { commerce: 'Supérette' } as Receipt;

async function open(options: { canShare?: boolean; copy?: boolean } = {}) {
  const width = signal<58 | 80>(58);
  const tickets = {
    width,
    setWidth: vi.fn((value: 58 | 80) => width.set(value)),
    canShare: options.canShare ?? true,
    print: vi.fn(),
    share: vi.fn().mockResolvedValue(true),
    copy: vi.fn().mockResolvedValue(options.copy ?? true),
  };
  TestBed.resetTestingModule();
  TestBed.configureTestingModule({ providers: [{ provide: TicketService, useValue: tickets }] });
  const fixture = TestBed.createComponent(TicketActions);
  fixture.componentRef.setInput('receipt', receipt);
  await fixture.whenStable();
  const root = fixture.nativeElement as HTMLElement;
  const click = async (label: string) => {
    [...root.querySelectorAll('button')].find((button) => button.textContent?.includes(label))!.click();
    await fixture.whenStable();
    fixture.detectChanges();
  };
  return { tickets, root, click, has: (text: string) => (root.textContent ?? '').includes(text) };
}

describe('TicketActions', () => {
  it('prints the ticket', async () => {
    const page = await open();

    await page.click('Imprimer le ticket');

    expect(page.tickets.print).toHaveBeenCalledWith(receipt);
  });

  it('shares the ticket when the device can share, and hides the button when it cannot', async () => {
    const page = await open();
    await page.click('Partager le ticket');
    expect(page.tickets.share).toHaveBeenCalledWith(receipt);

    expect((await open({ canShare: false })).has('Partager le ticket')).toBe(false);
  });

  it('copies the ticket and says what to do with it', async () => {
    const page = await open();

    await page.click('Copier le ticket');

    expect(page.tickets.copy).toHaveBeenCalledWith(receipt);
    expect(page.root.querySelector('[role="status"]')?.textContent).toContain('Ticket copié. Collez-le dans l’application de votre imprimante.');
  });

  it('says when the copy did not work', async () => {
    const page = await open({ copy: false });

    await page.click('Copier le ticket');

    expect(page.root.querySelector('[role="status"]')?.textContent).toContain('Impossible de copier');
  });

  it('lets the user choose the width of the paper and shows the current one', async () => {
    const page = await open();
    const select = page.root.querySelector<HTMLSelectElement>('#papier-ticket')!;
    expect(select.value).toBe('58');

    select.value = '80';
    select.dispatchEvent(new Event('change'));

    expect(page.tickets.setWidth).toHaveBeenCalledWith(80);
  });
});
