import { Component, inject, input, signal } from '@angular/core';
import { Receipt } from '../core/receipt';
import { TicketService } from '../services/ticket.service';

/**
 * Les trois sorties du ticket (TKT-07) : imprimer, partager vers l'application de l'imprimante Bluetooth, ou copier le texte ; et la largeur
 * du papier. Le partage n'apparaît que si l'appareil sait partager.
 */
@Component({
  selector: 'app-ticket-actions',
  template: `
    <div class="flex flex-wrap items-end gap-2">
      <button type="button" class="btn-primary" (click)="tickets.print(receipt())">Imprimer le ticket</button>
      @if (tickets.canShare) {
        <button type="button" class="btn" (click)="share()">Partager le ticket</button>
      }
      <button type="button" class="btn" (click)="copy()">Copier le ticket</button>
      <div class="ml-auto">
        <label class="field-label" for="papier-ticket">Papier</label>
        <select id="papier-ticket" class="field !min-h-11" (change)="tickets.setWidth(+$any($event.target).value === 80 ? 80 : 58)">
          <option value="58" [selected]="tickets.width() === 58">58 mm</option>
          <option value="80" [selected]="tickets.width() === 80">80 mm</option>
        </select>
      </div>
    </div>
    @if (notice()) {
      <p class="mt-2 font-semibold" [class.text-succes]="copied()" [class.text-avertissement]="!copied()" role="status">{{ notice() }}</p>
    }
  `,
})
export class TicketActions {
  protected readonly tickets = inject(TicketService);
  readonly receipt = input.required<Receipt>();
  protected readonly notice = signal('');
  protected readonly copied = signal(true);

  protected async share(): Promise<void> {
    await this.tickets.share(this.receipt());
  }

  protected async copy(): Promise<void> {
    const done = await this.tickets.copy(this.receipt());
    this.copied.set(done);
    this.notice.set(
      done
        ? 'Ticket copié. Collez-le dans l’application de votre imprimante.'
        : 'Impossible de copier automatiquement : utilisez « Imprimer » ou « Partager ».',
    );
  }
}

