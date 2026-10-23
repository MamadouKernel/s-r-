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
];
