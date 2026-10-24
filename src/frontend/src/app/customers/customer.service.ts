import { inject, Injectable, signal } from '@angular/core';
import { CustomerSnapshot } from '../domain/types';
import { JournalService } from '../services/journal.service';
import { CustomersApi } from './customers-api';

/**
 * Clients de la caisse : la vente à crédit n'utilise que cette copie locale (comme le catalogue), donc elle marche hors ligne. Créer un client
 * demande le réseau : le serveur garantit qu'un numéro ne sert qu'à un client.
 */
@Injectable({ providedIn: 'root' })
export class CustomerService {
  private readonly api = inject(CustomersApi);
  private readonly journalService = inject(JournalService);

  private readonly customersSignal = signal<CustomerSnapshot[]>([]);
  private readonly refreshFailedSignal = signal(false);

  readonly customers = this.customersSignal.asReadonly();
  readonly refreshFailed = this.refreshFailedSignal.asReadonly();

  async loadLocal(term?: string): Promise<void> {
    this.customersSignal.set(await (await this.journalService.get()).listCustomers(term));
  }

  /** Recopie la liste du serveur ; en cas d'échec la copie locale reste utilisable. Ne lève jamais : la caisse ne doit pas dépendre du réseau. */
  async refresh(): Promise<void> {
    try {
      await (await this.journalService.get()).replaceCustomers(await this.api.list(null));
      this.refreshFailedSignal.set(false);
    } catch {
      this.refreshFailedSignal.set(true);
    } finally {
      await this.loadLocal();
    }
  }

  /** Crée le client sur le serveur puis l'ajoute à la copie locale ; renvoie le client prêt à choisir. Lève une `ApiError` lisible. */
  async create(name: string, phone: string | null): Promise<CustomerSnapshot> {
    const id = crypto.randomUUID();
    const created = await this.api.create({ customerId: id, name: name.trim(), phone: phone?.trim() || null, creditCeilingFcfa: null });
    const snapshot: CustomerSnapshot = {
      id: created.customerId,
      name: name.trim(),
      phone: null,
      creditCeilingFcfa: created.creditCeilingFcfa,
      debtFcfa: 0,
      availableCreditFcfa: created.creditCeilingFcfa,
      isActive: true,
    };
    // Le téléphone est normalisé par le serveur : on relit la liste pour avoir la forme exacte, sans casser la copie si le réseau tombe.
    try {
      await (await this.journalService.get()).replaceCustomers(await this.api.list(null));
    } catch {
      await (await this.journalService.get()).upsertCustomer(snapshot);
    }
    await this.loadLocal();
    return this.customersSignal().find((customer) => customer.id === created.customerId) ?? snapshot;
  }
}
