import { HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { AuthService } from './auth.service';

/** Ajoute le jeton aux appels de l'API, sauf à la connexion elle-même. */
export const authInterceptor: HttpInterceptorFn = (request, next) => {
  const token = inject(AuthService).token();
  if (!token || !request.url.startsWith('/api/') || request.url.endsWith('/auth/login')) {
    return next(request);
  }
  return next(request.clone({ setHeaders: { Authorization: `Bearer ${token.value}` } }));
};
