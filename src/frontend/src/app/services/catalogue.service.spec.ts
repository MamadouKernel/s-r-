import 'fake-indexeddb/auto';
import { TestBed } from '@angular/core/testing';
import { Journal } from '../data/journal';
import { CatalogueProduct } from '../domain/types';
import { ApiService } from './api.service';
import { CatalogueService } from './catalogue.service';
import { JournalService } from './journal.service';

const product = (id: string, name: string): CatalogueProduct => ({
  id, name, barcode: null, category: null, salePriceFcfa: 100, tracksStock: false, quantityOnHand: 0,
});

describe('CatalogueService', () => {
  it('keeps the search on screen when a copy of the server catalogue finishes while the user is typing', async () => {
    const journal = await Journal.open(`soro-catalogue-${Math.random()}`);
    const server = [product('a', 'Riz parfumé · 25 kg'), product('b', 'Eau minérale · 1 L')];
    TestBed.configureTestingModule({
      providers: [
        { provide: JournalService, useValue: { get: () => Promise.resolve(journal) } },
        { provide: ApiService, useValue: { products: vi.fn().mockResolvedValue(server) } },
      ],
    });
    const catalogue = TestBed.inject(CatalogueService);
    await catalogue.refresh();
    await catalogue.loadLocal('riz');

    await catalogue.refresh();

    expect(catalogue.products().map((item) => item.id)).toEqual(['a']);
  });

  it('shows everything again once the search is cleared', async () => {
    const journal = await Journal.open(`soro-catalogue-${Math.random()}`);
    TestBed.configureTestingModule({
      providers: [
        { provide: JournalService, useValue: { get: () => Promise.resolve(journal) } },
        { provide: ApiService, useValue: { products: vi.fn().mockResolvedValue([product('a', 'Riz'), product('b', 'Eau')]) } },
      ],
    });
    const catalogue = TestBed.inject(CatalogueService);
    await catalogue.refresh();
    await catalogue.loadLocal('riz');

    await catalogue.loadLocal();
    await catalogue.refresh();

    expect(catalogue.products()).toHaveLength(2);
  });
});
