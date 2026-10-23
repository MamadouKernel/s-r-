import { Role } from './roles';

const LABELS: Record<Role, string> = {
  Owner: 'Propriétaire',
  Manager: 'Gérant',
  Cashier: 'Caissier',
  StockKeeper: 'Magasinier',
  Accountant: 'Comptable',
};

/** Profil en français (cahier des charges, section 3). Un profil inconnu du client est montré tel que le serveur l'envoie. */
export function roleLabel(role: string): string {
  return LABELS[role as Role] ?? role;
}
