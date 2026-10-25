import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { Router } from '@angular/router';
import { catchError, throwError } from 'rxjs';
import { FlashService } from '../management/flash.service';
import { AuthService } from './auth.service';

/**
 * Ajoute le jeton aux appels de l'API (sauf à la connexion elle-même) et réagit au retrait de l'appareil et au changement d'accès de l'utilisateur : dès qu'une requête est refusée parce
 * que le serveur ne reconnaît plus cet appareil, l'application l'oublie et renvoie à l'écran Bienvenue (ADR 0006).
 */
export const authInterceptor: HttpInterceptorFn = (request, next) => {
  const auth = inject(AuthService);
  const router = inject(Router);
  const flash = inject(FlashService);
  const token = auth.token();
  if (!token || !request.url.startsWith('/api/') || request.url.endsWith('/auth/login')) {
    return next(request);
  }
  return next(request.clone({ setHeaders: { Authorization: `Bearer ${token.value}` } })).pipe(
    catchError((error: unknown) => {
      if (error instanceof HttpErrorResponse && error.error?.code === 'device_not_authorized') {
        void auth.markDeviceRefused().then(() => router.navigateByUrl('/bienvenue'));
      }
      if (error instanceof HttpErrorResponse && error.error?.code === 'session_outdated') {
        // Le PIN, le rôle ou l'état du compte a changé : on referme la session, pas le journal (les ventes en attente restent à envoyer).
        void auth.logout().then(() => {
          flash.show('Votre accès a changé : reconnectez-vous.');
          return router.navigateByUrl('/connexion');
        });
      }
      return throwError(() => error);
    }),
  );
};
