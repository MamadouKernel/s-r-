import { canTestScanner, canManageCatalogue, canManageCustomers, canManageDevices, canOperateCustomers, canReadCustomers, canSeeRepayments, canManageStock, canManageTeam, canReadAuditLog, canSeeMargin, canViewReports } from './roles';

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

describe('customer rights', () => {
  const roles = ['Owner', 'Manager', 'Cashier', 'StockKeeper', 'Accountant', undefined];

  it('lets the till, the manager and the accountant read customers', () => {
    expect(roles.map(canReadCustomers)).toEqual([true, true, true, false, true, false]);
  });

  it('lets the till profiles create customers and collect repayments, not the accountant', () => {
    expect(roles.map(canOperateCustomers)).toEqual([true, true, true, false, false, false]);
  });

  it('lets only the owner and the manager edit ceilings and deactivate', () => {
    expect(roles.map(canManageCustomers)).toEqual([true, true, false, false, false, false]);
  });

  it('keeps the repayment history to those who manage or watch', () => {
    expect(roles.map(canSeeRepayments)).toEqual([true, true, false, false, true, false]);
  });
});

describe('roles: scanner', () => {
  it('lets the owner and the manager test the barcode reader', () => {
    expect(['Owner', 'Manager', 'StockKeeper', 'Cashier', 'Accountant', undefined].map(canTestScanner)).toEqual([
      true, true, false, false, false, false,
    ]);
  });
});
