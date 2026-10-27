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

  it('names the account and device actions without any technical word', () => {
    const labels = ['commerce.created', 'user.password.reset', 'device.invited', 'device.enrolled', 'device.revoked'].map(actionLabel);
    expect(labels).toEqual([
      'Boutique créée', 'Mot de passe du propriétaire changé', 'Lien d’ajout d’appareil créé', 'Appareil ajouté', 'Appareil retiré',
    ]);
  });

  it('names the credit and debt actions in plain words', () => {
    const labels = ['debt.repaid', 'credit.limit.approved', 'credit.limit.refused', 'credit.limit.exceeded'].map(actionLabel);
    expect(labels).toEqual([
      'Versement d’un client', 'Crédit au-delà du plafond validé', 'Crédit refusé : plafond atteint', 'Plafond de crédit dépassé',
    ]);
  });

  it('keeps an action the client does not know as the server sent it', () => {
    expect(actionLabel('user.created')).toBe('user.created');
  });
});

describe('CATEGORIES', () => {
  it('starts with “all” and uses the values the server understands', () => {
    expect(CATEGORIES[0]).toEqual({ value: null, label: 'Toutes' });
    expect(CATEGORIES.slice(1).map((category) => category.value)).toEqual(['sales', 'catalogue', 'stock', 'team', 'devices', 'credit', 'subscription', 'data', 'security']);
    expect(actionLabel('sale.returned')).toBe('Retour d’un client');
    expect(actionLabel('sale.return.refused')).toBe('Retour refusé');
    expect(actionLabel('login.offline')).toBe('Connexion sans réseau');
  });

  it('names the subscription, family and import actions in plain French', () => {
    expect(actionLabel('subscription.payment.declared')).toBe('Paiement d’abonnement déclaré');
    expect(actionLabel('subscription.payment.validated')).toBe('Paiement d’abonnement validé');
    expect(actionLabel('subscription.payment.refused')).toBe('Paiement d’abonnement refusé');
    expect(actionLabel('subscription.expired.sale')).toBe('Données reçues après la fin de l’abonnement');
    expect(actionLabel('data.exported')).toBe('Export des données');
    expect(actionLabel('products.imported')).toBe('Produits importés depuis Excel');
    expect(actionLabel('products.grouped')).toBe('Produits regroupés en une famille');
    expect(actionLabel('family.created')).toBe('Produit créé avec ses formats');
  });

  it('offers a subscription filter in the journal', () => {
    expect(CATEGORIES).toContainEqual({ value: 'subscription', label: 'Abonnement' });
  });

  it('names the team management actions in plain French', () => {
    const labels = ['user.pin.reset', 'user.enabled', 'user.role.changed', 'device.invitation.revoked'].map(actionLabel);
    expect(labels).toEqual(['PIN d’un membre changé', 'Compte réactivé', 'Rôle d’un membre changé', 'Lien d’ajout d’appareil annulé']);
  });

  it('names the creation of an internal code', () => {
    expect(actionLabel('product.barcode.created')).toBe('Code interne créé');
  });
});
