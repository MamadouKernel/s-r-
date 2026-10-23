import { actionLabel, CATEGORIES } from './audit-labels';

describe('actionLabel', () => {
  it('names every action the server writes today in French', () => {
    const actions = [
      'sale.discount.approved', 'sale.discount.refused', 'sale.cancelled', 'sale.cancel.refused',
      'product.created', 'product.updated', 'product.deactivated', 'stock.loss', 'user.disabled',
    ];
    expect(actions.map(actionLabel)).toEqual([
      'Remise approuvée', 'Remise refusée', 'Vente annulée', 'Annulation refusée',
      'Produit créé', 'Fiche produit modifiée', 'Produit désactivé', 'Perte de stock', 'Compte désactivé',
    ]);
  });

  it('keeps an action the client does not know as the server sent it', () => {
    expect(actionLabel('user.created')).toBe('user.created');
  });
});

describe('CATEGORIES', () => {
  it('starts with “all” and uses the values the server understands', () => {
    expect(CATEGORIES[0]).toEqual({ value: null, label: 'Toutes' });
    expect(CATEGORIES.slice(1).map((category) => category.value)).toEqual(['sales', 'catalogue', 'stock', 'team']);
  });
});
