import { inject, Injectable, signal } from '@angular/core';
import { buildReceipt, daySaleRank, Receipt } from '../core/receipt';
import { SaleOperation } from '../data/journal';
import { AuthService } from './auth.service';
import { JournalService } from './journal.service';

/**
 * Le ticket de la dernière vente de cet appareil (docs/specs/etiquettes-tickets TKT-01, TKT-11) : fabriqué à l'encaissement avec ce que l'appareil
 * connaît (lignes, paiements, caissier, nom du commerce, rang du jour), donc sans réseau ; il sert à imprimer et à réimprimer.
 */
@Injectable({ providedIn: 'root' })
export class ReceiptService {
  private readonly auth = inject(AuthService);
  private readonly journalService = inject(JournalService);
  private readonly lastReceipt = signal<Receipt | null>(null);

  readonly last = this.lastReceipt.asReadonly();

  async remember(sale: SaleOperation, customerName: string | null): Promise<Receipt> {
    const operations = await (await this.journalService.get()).operations();
    const times = operations.filter((operation): operation is SaleOperation => operation.kind === 'sale').map((operation) => operation.at);
    const device = this.auth.device();
    const receipt = buildReceipt(sale, {
      commerce: device?.commerceName ?? 'Boutique',
      cashier: this.auth.user()?.displayName ?? '',
      register: device?.name ?? 'Caisse',
      rank: daySaleRank(sale.at, times),
      customer: customerName ? { name: customerName } : null,
    });
    this.lastReceipt.set(receipt);
    return receipt;
  }
}
