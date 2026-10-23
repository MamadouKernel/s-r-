// Droits par profil, miroir de ce que le serveur applique (RolePolicy). L'interface ne fait que masquer : le serveur reste l'arbitre.

export type Role = 'Owner' | 'Manager' | 'Cashier' | 'StockKeeper' | 'Accountant';

/** Fiches produit et prix : propriétaire et gérant. */
export function canManageCatalogue(role: string | undefined): boolean {
  return role === 'Owner' || role === 'Manager';
}

/** Réceptions, pertes, historique et prix d'achat : propriétaire, gérant et magasinier. */
export function canManageStock(role: string | undefined): boolean {
  return role === 'Owner' || role === 'Manager' || role === 'StockKeeper';
}

/** Écran Équipe : propriétaire et gérant, ceux que le serveur autorise à lire et à gérer l'équipe. */
export function canManageTeam(role: string | undefined): boolean {
  return role === 'Owner' || role === 'Manager';
}
