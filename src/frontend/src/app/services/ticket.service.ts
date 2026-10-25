import { Injectable, signal } from '@angular/core';
import { Receipt, receiptDocument, receiptText, ticketColumns, TicketWidth } from '../core/receipt';

const WIDTH_KEY = 'soro.ticketWidth';
/** Garde-fou : une page d'impression oubliée (fenêtre fermée sans « afterprint ») est retirée après ce délai. */
const FRAME_LIFETIME_MS = 120_000;

/**
 * Sortie du ticket (docs/specs/etiquettes-tickets TKT-05, TKT-07) : impression par la fenêtre de l'appareil, partage ou copie du texte
 * (imprimantes Bluetooth des téléphones). L'application n'envoie jamais le ticket elle-même : l'utilisateur choisit où il va.
 */
@Injectable({ providedIn: 'root' })
export class TicketService {
  readonly width = signal<TicketWidth>(readWidth());
  readonly canShare = typeof navigator !== 'undefined' && typeof navigator.share === 'function';

  setWidth(width: TicketWidth): void {
    this.width.set(width);
    try {
      localStorage.setItem(WIDTH_KEY, String(width));
    } catch {
      // Stockage indisponible : le choix vaut pour cette session seulement.
    }
  }

  /** Imprime à travers une page cachée : le reste de l'écran n'est jamais mis en page avec le ticket. */
  print(receipt: Receipt): void {
    const frame = document.createElement('iframe');
    frame.title = 'Ticket de caisse';
    frame.setAttribute('aria-hidden', 'true');
    frame.style.cssText = 'position:fixed;right:0;bottom:0;width:0;height:0;border:0';
    frame.srcdoc = receiptDocument(receipt, this.width());
    frame.addEventListener('load', () => {
      const page = frame.contentWindow;
      if (!page) {
        return;
      }
      page.addEventListener('afterprint', () => frame.remove(), { once: true });
      page.focus();
      page.print();
    });
    document.body.append(frame);
    setTimeout(() => frame.remove(), FRAME_LIFETIME_MS);
  }

  /** Ouvre le menu de partage du téléphone avec le ticket en texte ; `false` si l'utilisateur le referme ou si le partage échoue. */
  async share(receipt: Receipt): Promise<boolean> {
    try {
      await navigator.share({ title: 'Ticket de caisse', text: this.text(receipt) });
      return true;
    } catch {
      return false;
    }
  }

  async copy(receipt: Receipt): Promise<boolean> {
    try {
      await navigator.clipboard.writeText(this.text(receipt));
      return true;
    } catch {
      return false;
    }
  }

  private text(receipt: Receipt): string {
    return receiptText(receipt, ticketColumns(this.width()));
  }
}

function readWidth(): TicketWidth {
  try {
    return localStorage.getItem(WIDTH_KEY) === '80' ? 80 : 58;
  } catch {
    return 58;
  }
}
