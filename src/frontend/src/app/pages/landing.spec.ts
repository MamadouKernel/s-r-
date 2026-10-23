import { landingPath } from './landing';

describe('landingPath (SC-D8)', () => {
  it('sends the owner and the accountant to the dashboard, even with a session open', () => {
    expect(landingPath('Owner', false)).toBe('/tableau-de-bord');
    expect(landingPath('Owner', true)).toBe('/tableau-de-bord');
    expect(landingPath('Accountant', false)).toBe('/tableau-de-bord');
  });

  it('keeps the register journey for the manager, the cashier and the stock keeper', () => {
    expect(landingPath('Manager', false)).toBe('/ouverture');
    expect(landingPath('Cashier', false)).toBe('/ouverture');
    expect(landingPath('Cashier', true)).toBe('/vente');
    expect(landingPath('StockKeeper', false)).toBe('/ouverture');
  });

  it('falls back to the opening screen when the profile is unknown', () => {
    expect(landingPath(undefined, false)).toBe('/ouverture');
  });
});
