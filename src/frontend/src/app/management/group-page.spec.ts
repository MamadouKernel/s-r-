import { ApiError } from '../sync/sync-api';
import { GroupPage } from './group-page';
import { fakeApi, product, render } from './test-support';

const items = [
  product({ id: 'a', name: 'Eau 1,5 L', quantityOnHand: 12 }),
  product({ id: 'b', name: 'Eau 50 cl', quantityOnHand: 7 }),
  product({ id: 'c', name: "Jus d'orange 1 L", quantityOnHand: 9 }),
  product({ id: 'd', name: 'Riz · 5 kg', familyId: 'riz', familyName: 'Riz', formatLabel: '5 kg' }),
  product({ id: 'e', name: 'Riz · 25 kg', familyId: 'riz', familyName: 'Riz', formatLabel: '25 kg' }),
  product({ id: 'f', name: 'Ancien', isActive: false }),
];

async function open(api = fakeApi()) {
  api.list.mockResolvedValue(items);
  return { api, page: await render(GroupPage, { api }) };
}

async function choose(page: Awaited<ReturnType<typeof render>>, ...labels: string[]) {
  for (const label of labels) {
    const box = [...page.root.querySelectorAll<HTMLInputElement>('input[type="checkbox"]')].find((input) => input.closest('label')?.textContent?.includes(label))!;
    box.checked = true;
    box.dispatchEvent(new Event('change'));
    await page.settle();
  }
}

describe('GroupPage', () => {
  it('proposes only the products that are alone in their family and still active', async () => {
    const { page } = await open();

    const names = [...page.root.querySelectorAll('fieldset label')].map((label) => label.textContent ?? '');
    expect(names).toHaveLength(3);
    expect(names.join(' ')).not.toContain('Riz');
    expect(names.join(' ')).not.toContain('Ancien');
  });

  it('groups the chosen products under the family name with a label for each', async () => {
    const { api, page } = await open();
    await choose(page, 'Eau 1,5 L', 'Eau 50 cl');
    await page.type('#nom', ' Eau minérale ');
    await page.type('#categorie', 'Boissons');
    await page.type('#format-0', '1,5 L');
    await page.type('#format-1', '50 cl');

    await page.submit();

    expect(api.group).toHaveBeenCalledTimes(1);
    expect(api.group.mock.calls[0][0]).toMatchObject({
      name: 'Eau minérale', category: 'Boissons',
      members: [{ productId: 'a', label: '1,5 L' }, { productId: 'b', label: '50 cl' }],
    });
    expect(api.group.mock.calls[0][0].familyId).toMatch(/^[0-9a-f-]{36}$/);
    expect(page.flash.current()).toBe('Produits regroupés.');
    expect(page.navigate).toHaveBeenCalledWith('/gestion/produits');
  });

  it('tells before confirming what the new names will be and that sales and stock do not change', async () => {
    const { page } = await open();
    await choose(page, 'Eau 1,5 L', 'Eau 50 cl');
    await page.type('#nom', 'Eau minérale');
    await page.type('#format-0', '1,5 L');
    await page.type('#format-1', '50 cl');

    const note = page.root.querySelector('[role="note"]')!.textContent!;
    expect(note).toContain('« Eau minérale · 1,5 L »');
    expect(note).toContain('« Eau minérale · 50 cl »');
    expect(note).toContain('Les anciennes ventes gardent leur ancien nom');
  });

  it('asks for two products, a name and a distinct label for each, and sends nothing', async () => {
    const { api, page } = await open();
    await choose(page, 'Eau 1,5 L');

    await page.submit();
    expect(page.root.textContent).toContain('Choisissez au moins deux produits');
    expect(page.root.querySelector('#nom-erreur')?.textContent).toContain('obligatoire');

    await choose(page, 'Eau 50 cl');
    await page.type('#nom', 'Eau');
    await page.type('#format-0', '1 L');
    await page.type('#format-1', '1 l');
    await page.submit();

    expect(api.group).not.toHaveBeenCalled();
    expect(page.root.querySelector('#format-1-erreur')?.textContent).toContain('déjà pris');
  });

  it('shows the server message and keeps the form when grouping fails', async () => {
    const { api, page } = await open();
    api.group.mockRejectedValue(new ApiError(422, 'rule_violation', '« Eau 1,5 L » fait déjà partie d’une famille de plusieurs formats.'));
    await choose(page, 'Eau 1,5 L', 'Eau 50 cl');
    await page.type('#nom', 'Eau');
    await page.type('#format-0', '1 L');
    await page.type('#format-1', '2 L');

    await page.submit();

    expect(page.root.querySelector('[role="alert"]')?.textContent).toContain('fait déjà partie');
    expect(page.navigate).not.toHaveBeenCalled();
  });

  it('keeps the same family identifier on a retry', async () => {
    const { api, page } = await open();
    api.group.mockRejectedValueOnce(new ApiError(0, 'network', ''));
    await choose(page, 'Eau 1,5 L', 'Eau 50 cl');
    await page.type('#nom', 'Eau');
    await page.type('#format-0', '1 L');
    await page.type('#format-1', '2 L');

    await page.submit();
    await page.submit();

    expect(api.group.mock.calls[1][0].familyId).toBe(api.group.mock.calls[0][0].familyId);
  });

  it('says there is nothing to group when fewer than two single products exist', async () => {
    const api = fakeApi();
    api.list.mockResolvedValue([items[0]]);
    const page = await render(GroupPage, { api });

    expect(page.has('Il faut au moins deux produits seuls')).toBe(true);
  });
});
