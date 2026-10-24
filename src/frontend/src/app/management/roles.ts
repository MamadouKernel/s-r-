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

/** Tableau de bord et rapports : propriétaire, gérant et comptable (lecture seule), comme le serveur. */
export function canViewReports(role: string | undefined): boolean {
  return role === 'Owner' || role === 'Manager' || role === 'Accountant';
}

/** Marge estimée : il faut lire les rapports ET voir les prix d'achat, donc propriétaire et gérant (pas le comptable). */
export function canSeeMargin(role: string | undefined): boolean {
  return role === 'Owner' || role === 'Manager';
}

/** Journal des actions sensibles : propriétaire et comptable, comme le serveur. Le gérant, qu'il surveille aussi, n'y a pas accès. */
export function canReadAuditLog(role: string | undefined): boolean {
  return role === 'Owner' || role === 'Accountant';
}

/** Écran Appareils (inviter, retirer) : propriétaire et gérant, comme le serveur. */
export function canManageDevices(role: string | undefined): boolean {
  return role === 'Owner' || role === 'Manager';
}
