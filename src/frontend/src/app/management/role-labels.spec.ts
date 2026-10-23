import { roleLabel } from './role-labels';

describe('roleLabel', () => {
  it('names the five profiles in French', () => {
    expect(['Owner', 'Manager', 'Cashier', 'StockKeeper', 'Accountant'].map(roleLabel)).toEqual([
      'Propriétaire', 'Gérant', 'Caissier', 'Magasinier', 'Comptable',
    ]);
  });

  it('keeps an unknown profile as the server sent it', () => {
    expect(roleLabel('Auditor')).toBe('Auditor');
  });
});
