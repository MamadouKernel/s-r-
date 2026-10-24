import { canManageCatalogue, canManageDevices, canManageStock, canManageTeam, canReadAuditLog, canSeeMargin, canViewReports } from './roles';

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

  it('lets the accountant read the reports without managing anything', () => {
    expect(['Owner', 'Manager', 'Accountant', 'StockKeeper', 'Cashier', undefined].map(canViewReports)).toEqual([
      true, true, true, false, false, false,
    ]);
    expect(canManageStock('Accountant')).toBe(false);
  });

  it('keeps the margin to the profiles that see purchase prices', () => {
    expect(['Owner', 'Manager', 'Accountant', 'StockKeeper', 'Cashier', undefined].map(canSeeMargin)).toEqual([
      true, true, false, false, false, false,
    ]);
  });

  it('keeps the journal to the owner and the accountant, not the manager it also watches', () => {
    expect(['Owner', 'Accountant', 'Manager', 'StockKeeper', 'Cashier', undefined].map(canReadAuditLog)).toEqual([
      true, true, false, false, false, false,
    ]);
  });
});

describe('canManageDevices', () => {
  it('lets only the owner and the manager add and remove devices', () => {
    expect(['Owner', 'Manager', 'StockKeeper', 'Cashier', 'Accountant', undefined].map(canManageDevices)).toEqual([
      true, true, false, false, false, false,
    ]);
  });
});
