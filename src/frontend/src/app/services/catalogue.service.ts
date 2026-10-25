import { inject, Injectable, signal } from '@angular/core';
import { CatalogueProduct } from '../domain/types';
import { ApiService } from './api.service';
import { JournalService } from './journal.service';

/** Catalogue local : la vente n'utilise jamais que cette copie (PWA-03). */
@Injectable({ providedIn: 'root' })
export class CatalogueService {
  private readonly api = inject(ApiService);
  private readonly journalService = inject(JournalService);

  private readonly productsSignal = signal<CatalogueProduct[]>([]);
  private readonly refreshingSignal = signal(false);
  private readonly refreshFailedSignal = signal(false);

  readonly products = this.productsSignal.asReadonly();
  readonly refreshing = this.refreshingSignal.asReadonly();
  readonly refreshFailed = this.refreshFailedSignal.asReadonly();

  /** Dernière recherche affichée : une recopie du serveur qui se termine pendant la saisie ne doit pas l'effacer. */
  private lastTerm: string | undefined;

  async loadLocal(term?: string): Promise<void> {
    this.lastTerm = term;
    this.productsSignal.set(await (await this.journalService.get()).listProducts(term));
  }

  /** Recopie le catalogue du serveur ; en cas d'échec la copie locale reste utilisable. */
  async refresh(): Promise<void> {
    this.refreshingSignal.set(true);
    try {
      const fresh = await this.api.products();
      await (await this.journalService.get()).replaceCatalogue(fresh);
      this.refreshFailedSignal.set(false);
    } catch {
      this.refreshFailedSignal.set(true);
    } finally {
      this.refreshingSignal.set(false);
      await this.loadLocal(this.lastTerm);
    }
  }

  async findByBarcode(code: string): Promise<CatalogueProduct | null> {
    return (await this.journalService.get()).findByBarcode(code);
  }
}
