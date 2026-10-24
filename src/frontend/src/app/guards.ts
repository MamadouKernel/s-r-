import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthService } from './services/auth.service';
import { canManageCatalogue, canManageDevices, canReadCustomers, canManageStock, canManageTeam, canReadAuditLog, canViewReports } from './management/roles';
import { CashService } from './services/cash.service';

/** L'appareil doit connaître son commerce (PWA-01). */
export const deviceGuard: CanActivateFn = () => {
  const router = inject(Router);
  const device = inject(AuthService).device();
  return device && !device.revoked ? true : router.parseUrl('/bienvenue');
};

/** L'écran Bienvenue n'a de sens que pour un appareil pas encore configuré, ou retiré (il le dit alors). */
export const welcomeGuard: CanActivateFn = () => {
  const router = inject(Router);
  const device = inject(AuthService).device();
  return device && !device.revoked ? router.parseUrl('/connexion') : true;
};

/** Écran Appareils : propriétaire et gérant seulement ; les autres profils retombent sur la liste des produits. */
export const devicesGuard: CanActivateFn = () => {
  const router = inject(Router);
  const auth = inject(AuthService);
  if (!auth.signedIn()) {
    return router.parseUrl('/connexion');
  }
  return canManageDevices(auth.user()?.role) ? true : router.parseUrl('/gestion/produits');
};

/**
 * Un utilisateur connecté, ou une session de caisse déjà ouverte sur l'appareil : jeton expiré n'empêche pas de vendre
 * (ADR 0004), la synchronisation attend simplement une reconnexion.
 */
export const userGuard: CanActivateFn = () => {
  const router = inject(Router);
  const auth = inject(AuthService);
  return auth.signedIn() || inject(CashService).session() ? true : router.parseUrl('/connexion');
};

export const openSessionGuard: CanActivateFn = () => {
  const router = inject(Router);
  return inject(CashService).session() ? true : router.parseUrl('/ouverture');
};

/**
 * Espace « Gestion » : il faut être connecté avec un profil qui gère le stock. Les écrans parlent directement au serveur, qui
 * applique les mêmes droits ; ce contrôle évite seulement d'afficher des pages inutilisables.
 */
export const managementGuard: CanActivateFn = () => {
  const router = inject(Router);
  const auth = inject(AuthService);
  if (!auth.signedIn()) {
    return router.parseUrl('/connexion');
  }
  return canManageStock(auth.user()?.role) ? true : router.parseUrl('/vente');
};

/** Création et modification des fiches produit : propriétaire et gérant seulement (le magasinier réceptionne mais ne change pas les prix). */
export const catalogueGuard: CanActivateFn = () => {
  const router = inject(Router);
  const auth = inject(AuthService);
  if (!auth.signedIn()) {
    return router.parseUrl('/connexion');
  }
  return canManageCatalogue(auth.user()?.role) ? true : router.parseUrl('/gestion/produits');
};

/** Écran Équipe : propriétaire et gérant seulement ; les autres profils retombent sur la liste des produits. */
export const teamGuard: CanActivateFn = () => {
  const router = inject(Router);
  const auth = inject(AuthService);
  if (!auth.signedIn()) {
    return router.parseUrl('/connexion');
  }
  return canManageTeam(auth.user()?.role) ? true : router.parseUrl('/gestion/produits');
};

/** Tableau de bord : les profils qui lisent les rapports. Un caissier retombe sur la caisse, où il a sa place. */
export const reportsGuard: CanActivateFn = () => {
  const router = inject(Router);
  const auth = inject(AuthService);
  if (!auth.signedIn()) {
    return router.parseUrl('/connexion');
  }
  return canViewReports(auth.user()?.role) ? true : router.parseUrl('/vente');
};

/** Clients et dettes : la caisse, le gérant et le comptable ; le magasinier retombe sur la liste des produits. */
export const customersGuard: CanActivateFn = () => {
  const router = inject(Router);
  const auth = inject(AuthService);
  if (!auth.signedIn()) {
    return router.parseUrl('/connexion');
  }
  return canReadCustomers(auth.user()?.role) ? true : router.parseUrl('/gestion/produits');
};

/** Journal des actions sensibles : propriétaire et comptable ; les autres profils retombent sur la caisse. */
export const auditGuard: CanActivateFn = () => {
  const router = inject(Router);
  const auth = inject(AuthService);
  if (!auth.signedIn()) {
    return router.parseUrl('/connexion');
  }
  return canReadAuditLog(auth.user()?.role) ? true : router.parseUrl('/vente');
};
