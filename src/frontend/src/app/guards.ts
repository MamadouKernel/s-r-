import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthService } from './services/auth.service';
import { CashService } from './services/cash.service';

/** L'appareil doit connaître son commerce (PWA-01). */
export const deviceGuard: CanActivateFn = () => {
  const router = inject(Router);
  return inject(AuthService).device() ? true : router.parseUrl('/appareil');
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
