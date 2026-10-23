/**
 * Où arrive-t-on après la connexion ? Le propriétaire et le comptable n'encaissent pas : ils arrivent sur le tableau de bord.
 * Le gérant, le caissier et le magasinier gardent leur parcours (reprendre la vente en cours, sinon ouvrir la caisse).
 */
export function landingPath(role: string | undefined, hasOpenSession: boolean): string {
  if (role === 'Owner' || role === 'Accountant') {
    return '/tableau-de-bord';
  }
  return hasOpenSession ? '/vente' : '/ouverture';
}
