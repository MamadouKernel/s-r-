import { Routes } from '@angular/router';
import { deviceGuard, openSessionGuard, userGuard } from './guards';

export const routes: Routes = [
  { path: 'appareil', loadComponent: () => import('./pages/device-page').then((m) => m.DevicePage) },
  {
    path: 'connexion',
    canActivate: [deviceGuard],
    loadComponent: () => import('./pages/login-page').then((m) => m.LoginPage),
  },
  {
    path: 'ouverture',
    canActivate: [deviceGuard, userGuard],
    loadComponent: () => import('./pages/open-session-page').then((m) => m.OpenSessionPage),
  },
  {
    path: 'vente',
    canActivate: [deviceGuard, userGuard, openSessionGuard],
    loadComponent: () => import('./pages/sale-page').then((m) => m.SalePage),
  },
  {
    path: 'file',
    canActivate: [deviceGuard],
    loadComponent: () => import('./pages/queue-page').then((m) => m.QueuePage),
  },
  {
    path: 'cloture',
    canActivate: [deviceGuard, userGuard, openSessionGuard],
    loadComponent: () => import('./pages/close-page').then((m) => m.ClosePage),
  },
  { path: '', pathMatch: 'full', redirectTo: 'vente' },
  { path: '**', redirectTo: 'vente' },
];
