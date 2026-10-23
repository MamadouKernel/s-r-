import { Routes } from '@angular/router';
import { catalogueGuard, deviceGuard, managementGuard, openSessionGuard, reportsGuard, teamGuard, userGuard } from './guards';

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
  {
    path: 'tableau-de-bord',
    canActivate: [deviceGuard, reportsGuard],
    loadComponent: () => import('./dashboard/dashboard-page').then((m) => m.DashboardPage),
  },
  {
    path: 'gestion',
    canActivate: [deviceGuard, managementGuard],
    loadComponent: () => import('./management/management-shell').then((m) => m.ManagementShell),
    children: [
      { path: '', pathMatch: 'full', redirectTo: 'produits' },
      {
        path: 'produits',
        loadComponent: () => import('./management/products-page').then((m) => m.ProductsPage),
      },
      {
        path: 'produits/nouveau',
        canActivate: [catalogueGuard],
        loadComponent: () => import('./management/product-form-page').then((m) => m.ProductFormPage),
      },
      {
        path: 'produits/:id',
        canActivate: [catalogueGuard],
        loadComponent: () => import('./management/product-form-page').then((m) => m.ProductFormPage),
      },
      {
        path: 'produits/:id/stock',
        loadComponent: () => import('./management/product-stock-page').then((m) => m.ProductStockPage),
      },
      {
        path: 'equipe',
        canActivate: [teamGuard],
        loadComponent: () => import('./management/team-page').then((m) => m.TeamPage),
      },
      {
        path: 'equipe/nouveau',
        canActivate: [teamGuard],
        loadComponent: () => import('./management/member-form-page').then((m) => m.MemberFormPage),
      },
    ],
  },
  { path: '', pathMatch: 'full', redirectTo: 'vente' },
  { path: '**', redirectTo: 'vente' },
];
