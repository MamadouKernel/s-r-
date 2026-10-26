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

/** Clients, dettes et plafonds : la caisse (qui vend à crédit), le gérant et le comptable, comme le serveur. */
export function canReadCustomers(role: string | undefined): boolean {
  return role === 'Owner' || role === 'Manager' || role === 'Cashier' || role === 'Accountant';
}

/** Encaisser un versement et créer un client : les profils de caisse (propriétaire, gérant, caissier). */
export function canOperateCustomers(role: string | undefined): boolean {
  return role === 'Owner' || role === 'Manager' || role === 'Cashier';
}

/** Modifier un client (nom, téléphone, plafond), le désactiver : propriétaire et gérant. Au-delà du plafond, seul un gérant valide. */
export function canManageCustomers(role: string | undefined): boolean {
  return role === 'Owner' || role === 'Manager';
}

/** Historique des versements d'un client : propriétaire, gérant et comptable. */
export function canSeeRepayments(role: string | undefined): boolean {
  return role === 'Owner' || role === 'Manager' || role === 'Accountant';
}

/** Écran Appareils (inviter, retirer) : propriétaire et gérant, comme le serveur. */
export function canManageDevices(role: string | undefined): boolean {
  return role === 'Owner' || role === 'Manager';
}

/** Export de toutes les données (téléphones des clients, prix d'achat) : le propriétaire seul, comme le serveur. */
export function canExportData(role: string | undefined): boolean {
  return role === 'Owner';
}

/** Écran « Lecteur de codes-barres » (test du lecteur) : propriétaire et gérant, ceux qui équipent la caisse. */
export function canTestScanner(role: string | undefined): boolean {
  return role === 'Owner' || role === 'Manager';
}

/** Écran « Étiquettes » (imprimer des étiquettes de produits) : ceux qui gèrent le stock, comme le serveur. */
export function canPrintLabels(role: string | undefined): boolean {
  return role === 'Owner' || role === 'Manager' || role === 'StockKeeper';
}
