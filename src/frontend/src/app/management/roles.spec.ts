import { canManageCatalogue, canManageStock, canManageTeam } from './roles';

describe('roles', () => {
  it('lets only the owner and the manager manage the catalogue', () => {
    expect(['Owner', 'Manager', 'StockKeeper', 'Cashier', 'Accountant', undefined].map(canManageCatalogue)).toEqual([
      true, true, false, false, false, false,
    ]);
  });

  it('adds the stock keeper for the stock operations', () => {
    expect(['Owner', 'Manager', 'StockKeeper', 'Cashier', 'Accountant', undefined].map(canManageStock)).toEqual([
      true, true, true, false, false, false,
    ]);
  });

  it('keeps the team screen to the owner and the manager', () => {
    expect(['Owner', 'Manager', 'StockKeeper', 'Cashier', 'Accountant', undefined].map(canManageTeam)).toEqual([
      true, true, false, false, false, false,
    ]);
  });
});
