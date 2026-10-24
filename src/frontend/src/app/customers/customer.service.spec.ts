import 'fake-indexeddb/auto';
import { TestBed } from '@angular/core/testing';
import { Journal } from '../data/journal';
import { CustomerSnapshot } from '../domain/types';
import { JournalService } from '../services/journal.service';
import { ApiError } from '../sync/sync-api';
import { CustomerService } from './customer.service';
import { CustomersApi } from './customers-api';

const awa: CustomerSnapshot = { id: 'c-awa', name: 'Awa Koné', phone: '+2250707070707', creditCeilingFcfa: 20_000, debtFcfa: 8_000, availableCreditFcfa: 12_000, isActive: true };

async function setup(api: Partial<Record<'list' | 'create', ReturnType<typeof vi.fn>>> = {}) {
  const journal = await Journal.open(`soro-customers-${Math.random()}`);
  const fake = { list: vi.fn().mockResolvedValue([awa]), create: vi.fn(), ...api };
  TestBed.configureTestingModule({
    providers: [
      { provide: JournalService, useValue: { get: () => Promise.resolve(journal) } },
      { provide: CustomersApi, useValue: fake },
    ],
  });
  return { service: TestBed.inject(CustomerService), journal, api: fake };
}

describe('CustomerService', () => {
  it('copies the server list to the device on refresh', async () => {
    const { service, journal } = await setup();

    await service.refresh();

    expect(service.customers()).toEqual([awa]);
    expect(await journal.listCustomers()).toEqual([awa]);
    expect(service.refreshFailed()).toBe(false);
  });

  it('keeps the local copy and never throws when the network is down', async () => {
    const { service, journal, api } = await setup();
    await journal.replaceCustomers([awa]);
    api.list.mockRejectedValue(new ApiError(0, 'network', ''));

    await expect(service.refresh()).resolves.toBeUndefined();

    expect(service.refreshFailed()).toBe(true);
    expect(service.customers()).toEqual([awa]);
  });

  it('creates a customer with a fresh id and no ceiling, then returns it with the exact phone the server kept', async () => {
    const { service, api } = await setup();
    api.create.mockResolvedValue({ customerId: 'c-new', alreadyProcessed: false, creditCeilingFcfa: 20_000 });
    api.list.mockResolvedValue([awa, { ...awa, id: 'c-new', name: 'Marie Awa', phone: '+2250102030405', debtFcfa: 0, availableCreditFcfa: 20_000 }]);

    const created = await service.create('  Marie Awa ', ' 01 02 03 04 05 ');

    expect(api.create).toHaveBeenCalledWith({ customerId: expect.stringMatching(/^[0-9a-f-]{36}$/), name: 'Marie Awa', phone: '01 02 03 04 05', creditCeilingFcfa: null });
    expect(created).toMatchObject({ id: 'c-new', name: 'Marie Awa', phone: '+2250102030405' });
    expect(service.customers().map((c) => c.id)).toEqual(['c-awa', 'c-new']);
  });

  it('still returns the new customer when the list cannot be read right after creating it', async () => {
    const { service, api } = await setup();
    api.create.mockResolvedValue({ customerId: 'c-new', alreadyProcessed: false, creditCeilingFcfa: 20_000 });
    api.list.mockRejectedValue(new ApiError(0, 'network', ''));

    const created = await service.create('Marie Awa', null);

    expect(created).toMatchObject({ id: 'c-new', name: 'Marie Awa', debtFcfa: 0, availableCreditFcfa: 20_000 });
    expect(service.customers().map((c) => c.id)).toEqual(['c-new']);
  });

  it('lets the server refusal through, in its own words', async () => {
    const { service, api } = await setup();
    api.create.mockRejectedValue(new ApiError(409, 'phone_taken', 'Le numéro +225 07 ** ** ** 07 est déjà celui d’un autre client.'));

    await expect(service.create('Autre Awa', '0707070707')).rejects.toMatchObject({ code: 'phone_taken' });
  });

  it('filters the local list while typing', async () => {
    const { service, journal } = await setup();
    await journal.replaceCustomers([awa, { ...awa, id: 'c-2', name: 'Issa Diallo', phone: null }]);

    await service.loadLocal('issa');

    expect(service.customers().map((c) => c.name)).toEqual(['Issa Diallo']);
  });
});
