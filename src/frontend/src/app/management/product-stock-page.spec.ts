import { ApiError } from '../sync/sync-api';
import { ProductStockPage } from './product-stock-page';
import { fakeApi, fakeTeamApi, product, render } from './test-support';

const movement = (overrides = {}) => ({
  id: 'm-1', kind: 'Reception', quantityDelta: 24, reason: 'Grossiste Koné', sourceId: null,
  userId: '0a1b2c3d-aaaa-bbbb-cccc-111122223333', at: '2026-10-23T09:00:00.000Z', ...overrides,
});

async function open(role = 'StockKeeper', stock = 24) {
  const api = fakeApi();
  api.list.mockResolvedValue([product({ id: 'p-1', quantityOnHand: stock })]);
  api.movements.mockResolvedValue([movement()]);
  const page = await render(ProductStockPage, { api, role, params: { id: 'p-1' } });
  return { api, page };
}

describe('ProductStockPage', () => {
  it('shows the stock, the history with signed quantities and the author', async () => {
    const { page } = await open();

    expect(page.has("Jus d'orange 1L")).toBe(true);
    expect(page.has('Stock : 24')).toBe(true);
    expect(page.has('Réception')).toBe(true);
    expect(page.has('+24')).toBe(true);
    expect(page.has('Grossiste Koné')).toBe(true);
    expect(page.has('0a1b2c3d')).toBe(true);
  });

  it('receives goods with a stable movement id, confirms the new stock and refreshes (SC-G3)', async () => {
    const { api, page } = await open();
    api.receive.mockResolvedValue({ movementId: 'm', quantityOnHand: 48, alreadyProcessed: false });
    api.list.mockResolvedValue([product({ id: 'p-1', quantityOnHand: 48 })]);
    await page.type('#reception-quantite', '24');
    await page.type('#reception-fournisseur', ' Grossiste Koné ');
    await page.type('#reception-prix', '450');

    await page.submit(0);

    expect(api.receive).toHaveBeenCalledWith('p-1', {
      movementId: expect.stringMatching(/^[0-9a-f-]{36}$/), quantity: 24, supplier: 'Grossiste Koné', purchasePriceFcfa: 450,
    });
    expect(page.has('Réception enregistrée. Stock : 48.')).toBe(true);
    expect(page.has('Stock : 48')).toBe(true);
    expect(page.root.querySelector<HTMLInputElement>('#reception-quantite')!.value).toBe('');
  });

  it('refuses a missing or zero quantity on a reception', async () => {
    const { api, page } = await open();
    await page.type('#reception-quantite', '0');
    await page.submit(0);
    expect(api.receive).not.toHaveBeenCalled();
    expect(page.has('supérieure à zéro')).toBe(true);
  });

  it('keeps the same movement id when a reception is retried after a network failure', async () => {
    const { api, page } = await open();
    api.receive.mockRejectedValueOnce(new ApiError(0, 'network', ''));
    await page.type('#reception-quantite', '10');

    await page.submit(0);
    expect(page.has('injoignable')).toBe(true);
    await page.submit(0);

    expect(api.receive.mock.calls[1][1].movementId).toBe(api.receive.mock.calls[0][1].movementId);
  });

  it('records a loss with its reason (SC-G3)', async () => {
    const { api, page } = await open();
    api.recordLoss.mockResolvedValue({ movementId: 'm', quantityOnHand: 22, alreadyProcessed: false });
    await page.type('#perte-quantite', '2');
    await page.type('#perte-motif', ' Périmé ');

    await page.submit(1);

    expect(api.recordLoss).toHaveBeenCalledWith('p-1', { movementId: expect.any(String), quantity: 2, reason: 'Périmé' });
    expect(page.has('Perte enregistrée. Stock : 22.')).toBe(true);
  });

  it('refuses a loss without a reason, or above the available stock, before calling the server (SC-G4)', async () => {
    const { api, page } = await open('StockKeeper', 3);
    await page.type('#perte-quantite', '2');
    await page.submit(1);
    expect(page.has('Le motif de la perte est obligatoire.')).toBe(true);

    await page.type('#perte-quantite', '4');
    await page.type('#perte-motif', 'Cassé');
    await page.submit(1);
    expect(page.has('dépasse le stock disponible (3)')).toBe(true);
    expect(api.recordLoss).not.toHaveBeenCalled();
  });

  it('shows the server refusal of a loss in French under the form', async () => {
    const { api, page } = await open();
    api.recordLoss.mockRejectedValue(new ApiError(422, 'rule_violation', 'La perte dépasse le stock disponible.'));
    await page.type('#perte-quantite', '2');
    await page.type('#perte-motif', 'Cassé');

    await page.submit(1);

    expect(page.root.querySelector('#perte-motif')!.closest('form')!.querySelector('[role="alert"]')?.textContent).toContain(
      'dépasse le stock disponible',
    );
  });

  it('names the author of each movement from the directory (SC-E7)', async () => {
    const api = fakeApi();
    api.list.mockResolvedValue([product({ id: 'p-1' })]);
    api.movements.mockResolvedValue([movement()]);
    const teamApi = fakeTeamApi();
    teamApi.directory.mockResolvedValue([{ id: '0a1b2c3d-aaaa-bbbb-cccc-111122223333', displayName: 'Moussa Traoré' }]);

    const page = await render(ProductStockPage, { api, teamApi, role: 'StockKeeper', params: { id: 'p-1' } });

    expect(page.has('Moussa Traoré')).toBe(true);
    expect(page.has('0a1b2c3d')).toBe(false);
    expect(page.has('Les noms des auteurs ne sont pas disponibles')).toBe(false);
  });

  it('falls back to the short identifier, and says so, when the directory cannot be read', async () => {
    const api = fakeApi();
    api.list.mockResolvedValue([product({ id: 'p-1' })]);
    api.movements.mockResolvedValue([movement()]);
    const teamApi = fakeTeamApi();
    teamApi.directory.mockRejectedValue(new ApiError(403, 'forbidden', ''));

    const page = await render(ProductStockPage, { api, teamApi, role: 'StockKeeper', params: { id: 'p-1' } });

    expect(page.has('0a1b2c3d')).toBe(true);
    expect(page.has('Les noms des auteurs ne sont pas disponibles')).toBe(true);
  });

  it('keeps the history table inside a positioned scrolling container', async () => {
    const { page } = await open();
    expect(page.root.querySelector('table')!.closest('div')!.classList.contains('relative')).toBe(true);
  });

  it('hides the purchase price field from a profile that does not receive it', async () => {
    const api = fakeApi();
    api.list.mockResolvedValue([product({ id: 'p-1', purchasePriceFcfa: null })]);
    const page = await render(ProductStockPage, { api, params: { id: 'p-1' } });
    expect(page.root.querySelector('#reception-prix')).toBeNull();
  });

  it('signals a product out of stock in words', async () => {
    const { page } = await open('Manager', 0);
    expect(page.has('rupture de stock')).toBe(true);
  });

  it('says so when the product does not exist', async () => {
    const api = fakeApi();
    api.list.mockResolvedValue([]);
    const page = await render(ProductStockPage, { api, params: { id: 'inconnu' } });
    expect(page.root.querySelector('[role="alert"]')?.textContent).toContain('Produit introuvable');
  });
});
