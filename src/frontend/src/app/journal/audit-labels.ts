const ACTIONS: Record<string, string> = {
  'sale.discount.approved': 'Remise approuvée',
  'sale.discount.refused': 'Remise refusée',
  'sale.cancelled': 'Vente annulée',
  'sale.cancel.refused': 'Annulation refusée',
  'product.created': 'Produit créé',
  'product.updated': 'Fiche produit modifiée',
  'product.deactivated': 'Produit désactivé',
  'stock.loss': 'Perte de stock',
  'user.disabled': 'Compte désactivé',
  'user.pin.reset': 'PIN d’un membre changé',
  'user.enabled': 'Compte réactivé',
  'user.role.changed': 'Rôle d’un membre changé',
  'user.password.reset': 'Mot de passe du propriétaire changé',
  'commerce.created': 'Boutique créée',
  'device.invited': 'Lien d’ajout d’appareil créé',
  'device.enrolled': 'Appareil ajouté',
  'device.revoked': 'Appareil retiré',
  'device.invitation.revoked': 'Lien d’ajout d’appareil annulé',
  'debt.repaid': 'Versement d’un client',
  'credit.limit.approved': 'Crédit au-delà du plafond validé',
  'credit.limit.refused': 'Crédit refusé : plafond atteint',
  'credit.limit.exceeded': 'Plafond de crédit dépassé',
  'family.created': 'Produit créé avec ses formats',
  'products.grouped': 'Produits regroupés en une famille',
  'products.imported': 'Produits importés depuis Excel',
  'data.exported': 'Export des données',
  'subscription.payment.declared': 'Paiement d’abonnement déclaré',
  'subscription.payment.validated': 'Paiement d’abonnement validé',
  'subscription.payment.refused': 'Paiement d’abonnement refusé',
  'subscription.expired.sale': 'Données reçues après la fin de l’abonnement',
};

/** Action en français ; une action que le client ne connaît pas (ajoutée plus tard par le serveur) est montrée telle quelle. */
export function actionLabel(action: string): string {
  return ACTIONS[action] ?? action;
}

/** Les filtres du journal : valeur envoyée au serveur et libellé. `null` : toutes les actions. */
export const CATEGORIES: readonly { value: string | null; label: string }[] = [
  { value: null, label: 'Toutes' },
  { value: 'sales', label: 'Remises et annulations' },
  { value: 'catalogue', label: 'Produits et prix' },
  { value: 'stock', label: 'Pertes de stock' },
  { value: 'team', label: 'Équipe' },
  { value: 'devices', label: 'Appareils' },
  { value: 'credit', label: 'Crédit et dettes' },
  { value: 'subscription', label: 'Abonnement' },
  { value: 'data', label: 'Export de données' },
];
