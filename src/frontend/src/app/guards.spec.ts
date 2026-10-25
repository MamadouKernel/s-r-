import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router, UrlTree } from '@angular/router';
import { auditGuard, catalogueGuard, customersGuard, deviceGuard, devicesGuard, managementGuard, reportsGuard, scannerGuard, teamGuard, welcomeGuard } from './guards';
import { AuthService } from './services/auth.service';

function run(signedIn: boolean, role: string | undefined, guard = managementGuard) {
  // Un module neuf à chaque appel : plusieurs profils sont essayés dans un même test.
  TestBed.resetTestingModule();
  TestBed.configureTestingModule({
    providers: [
      provideRouter([]),
      {
        provide: AuthService,
        useValue: { signedIn: signal(signedIn), user: signal(role ? { id: 'u', displayName: 'X', role } : null) },
      },
    ],
  });
  const result = TestBed.runInInjectionContext(() => guard({} as never, {} as never));
  return result instanceof UrlTree ? TestBed.inject(Router).serializeUrl(result) : result;
}

describe('managementGuard', () => {
  it('lets the owner, the manager and the stock keeper in', () => {
    expect(['Owner', 'Manager', 'StockKeeper'].map((role) => run(true, role))).toEqual([true, true, true]);
  });

  it('sends a cashier or an accountant back to the sale screen (SC-G5)', () => {
    expect(run(true, 'Cashier')).toBe('/vente');
    expect(run(true, 'Accountant')).toBe('/vente');
  });

  it('sends a signed out visitor to the login', () => {
    expect(run(false, undefined)).toBe('/connexion');
    expect(run(false, 'Owner')).toBe('/connexion');
  });
});

describe('catalogueGuard', () => {
  it('lets only the owner and the manager edit product sheets', () => {
    expect(['Owner', 'Manager'].map((role) => run(true, role, catalogueGuard))).toEqual([true, true]);
  });

  it('keeps the stock keeper on the product list, where he can still receive goods', () => {
    expect(run(true, 'StockKeeper', catalogueGuard)).toBe('/gestion/produits');
  });

  it('sends a signed out visitor to the login', () => {
    expect(run(false, undefined, catalogueGuard)).toBe('/connexion');
  });
});

describe('teamGuard', () => {
  it('lets the owner and the manager in', () => {
    expect(['Owner', 'Manager'].map((role) => run(true, role, teamGuard))).toEqual([true, true]);
  });

  it('keeps the stock keeper out of the team screen (SC-E3)', () => {
    expect(run(true, 'StockKeeper', teamGuard)).toBe('/gestion/produits');
  });

  it('sends a signed out visitor to the login', () => {
    expect(run(false, undefined, teamGuard)).toBe('/connexion');
  });
});

describe('reportsGuard', () => {
  it('lets the owner, the manager and the accountant read the dashboard', () => {
    expect(['Owner', 'Manager', 'Accountant'].map((role) => run(true, role, reportsGuard))).toEqual([true, true, true]);
  });

  it('keeps cashiers and stock keepers out and sends them to the sale screen (SC-D5)', () => {
    expect(run(true, 'Cashier', reportsGuard)).toBe('/vente');
    expect(run(true, 'StockKeeper', reportsGuard)).toBe('/vente');
  });

  it('sends a signed out visitor to the login', () => {
    expect(run(false, undefined, reportsGuard)).toBe('/connexion');
  });
});

describe('auditGuard', () => {
  it('lets the owner and the accountant read the journal', () => {
    expect(['Owner', 'Accountant'].map((role) => run(true, role, auditGuard))).toEqual([true, true]);
  });

  it('keeps the manager, the cashier and the stock keeper out (SC-M8)', () => {
    for (const role of ['Manager', 'Cashier', 'StockKeeper']) {
      expect(run(true, role, auditGuard)).toBe('/vente');
    }
  });

  it('sends a signed out visitor to the login', () => {
    expect(run(false, undefined, auditGuard)).toBe('/connexion');
  });
});

function runWithDevice(device: object | null, guard: typeof deviceGuard) {
  TestBed.resetTestingModule();
  TestBed.configureTestingModule({ providers: [provideRouter([]), { provide: AuthService, useValue: { device: signal(device) } }] });
  const result = TestBed.runInInjectionContext(() => guard({} as never, {} as never));
  return result instanceof UrlTree ? TestBed.inject(Router).serializeUrl(result) : result;
}

describe('deviceGuard', () => {
  it('lets a configured device through and sends the others to the welcome screen', () => {
    expect(runWithDevice({ tenantId: 't' }, deviceGuard)).toBe(true);
    expect(runWithDevice(null, deviceGuard)).toBe('/bienvenue');
  });

  it('sends a device the server removed back to the welcome screen, which tells it so', () => {
    expect(runWithDevice({ tenantId: 't', revoked: true }, deviceGuard)).toBe('/bienvenue');
  });
});

describe('welcomeGuard', () => {
  it('shows the welcome screen to a new or removed device and skips it for a configured one', () => {
    expect(runWithDevice(null, welcomeGuard)).toBe(true);
    expect(runWithDevice({ tenantId: 't', revoked: true }, welcomeGuard)).toBe(true);
    expect(runWithDevice({ tenantId: 't' }, welcomeGuard)).toBe('/connexion');
  });
});

describe('devicesGuard', () => {
  it('lets only the owner and the manager manage devices', () => {
    expect(['Owner', 'Manager'].map((role) => run(true, role, devicesGuard))).toEqual([true, true]);
    expect(['Cashier', 'StockKeeper', 'Accountant'].map((role) => run(true, role, devicesGuard))).toEqual([
      '/gestion/produits', '/gestion/produits', '/gestion/produits',
    ]);
    expect(run(false, undefined, devicesGuard)).toBe('/connexion');
  });
});

describe('customersGuard', () => {
  it('lets the till profiles, the manager and the accountant read customers and debts', () => {
    expect(['Owner', 'Manager', 'Cashier', 'Accountant'].map((role) => run(true, role, customersGuard))).toEqual([true, true, true, true]);
  });

  it('sends a stock keeper back to the products and a signed out visitor to the login', () => {
    expect(run(true, 'StockKeeper', customersGuard)).toBe('/gestion/produits');
    expect(run(false, undefined, customersGuard)).toBe('/connexion');
  });
});

describe('scannerGuard', () => {
  it('lets only the owner and the manager test the barcode reader', () => {
    expect(['Owner', 'Manager'].map((role) => run(true, role, scannerGuard))).toEqual([true, true]);
    expect(['Cashier', 'StockKeeper', 'Accountant'].map((role) => run(true, role, scannerGuard))).toEqual([
      '/gestion/produits', '/gestion/produits', '/gestion/produits',
    ]);
    expect(run(false, undefined, scannerGuard)).toBe('/connexion');
  });
});
