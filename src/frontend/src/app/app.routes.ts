import { Routes } from '@angular/router';
import { auditGuard, catalogueGuard, customersGuard, deviceGuard, devicesGuard, exportGuard, managementGuard, openSessionGuard, reportsGuard, scannerGuard, teamGuard, userGuard, welcomeGuard, labelsGuard, salesGuard } from './guards';

export const routes: Routes = [
  { path: 'appareil', pathMatch: 'full', redirectTo: 'bienvenue' },
  {
    path: 'bienvenue',
    canActivate: [welcomeGuard],
    loadComponent: () => import('./onboarding/welcome-page').then((m) => m.WelcomePage),
  },
  { path: 'inscription', loadComponent: () => import('./onboarding/signup-page').then((m) => m.SignupPage) },
  { path: 'connexion-proprietaire', loadComponent: () => import('./onboarding/owner-login-page').then((m) => m.OwnerLoginPage) },
  { path: 'mot-de-passe-oublie', loadComponent: () => import('./onboarding/password-reset-page').then((m) => m.PasswordResetPage) },
  { path: 'rejoindre', loadComponent: () => import('./onboarding/join-page').then((m) => m.JoinPage) },
  { path: 'conditions', loadComponent: () => import('./onboarding/conditions-page').then((m) => m.ConditionsPage) },
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
    path: 'ventes',
    canActivate: [deviceGuard, salesGuard],
    loadComponent: () => import('./sales/sales-page').then((m) => m.SalesPage),
  },
  {
    path: 'ventes/:id',
    canActivate: [deviceGuard, salesGuard],
    loadComponent: () => import('./sales/sale-detail-page').then((m) => m.SaleDetailPage),
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
    path: 'mouvements',
    canActivate: [deviceGuard, userGuard, openSessionGuard],
    loadComponent: () => import('./pages/movements-page').then((m) => m.MovementsPage),
  },
  {
    path: 'rapport',
    canActivate: [deviceGuard, reportsGuard],
    loadComponent: () => import('./report/day-report-page').then((m) => m.DayReportPage),
  },
  {
    path: 'tableau-de-bord',
    canActivate: [deviceGuard, reportsGuard],
    loadComponent: () => import('./dashboard/dashboard-page').then((m) => m.DashboardPage),
  },
  {
    path: 'clients',
    canActivate: [deviceGuard, customersGuard],
    loadComponent: () => import('./customers/customers-page').then((m) => m.CustomersPage),
  },
  {
    path: 'journal',
    canActivate: [deviceGuard, auditGuard],
    loadComponent: () => import('./journal/journal-page').then((m) => m.JournalPage),
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
        loadComponent: () => import('./management/family-form-page').then((m) => m.FamilyFormPage),
      },
      {
        path: 'produits/importer',
        canActivate: [catalogueGuard],
        loadComponent: () => import('./management/import-page').then((m) => m.ImportPage),
      },
      {
        path: 'produits/regrouper',
        canActivate: [catalogueGuard],
        loadComponent: () => import('./management/group-page').then((m) => m.GroupPage),
      },
      {
        path: 'produits/famille/:familyId',
        canActivate: [catalogueGuard],
        loadComponent: () => import('./management/family-page').then((m) => m.FamilyPage),
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
        path: 'export',
        canActivate: [exportGuard],
        loadComponent: () => import('./export/export-page').then((m) => m.ExportPage),
      },
      {
        path: 'abonnement',
        canActivate: [teamGuard],
        loadComponent: () => import('./subscription/subscription-page').then((m) => m.SubscriptionPage),
      },
      {
        path: 'equipe',
        canActivate: [teamGuard],
        loadComponent: () => import('./management/team-page').then((m) => m.TeamPage),
      },
      {
        path: 'appareils',
        canActivate: [devicesGuard],
        loadComponent: () => import('./management/devices-page').then((m) => m.DevicesPage),
      },
      {
        path: 'lecteur',
        canActivate: [scannerGuard],
        loadComponent: () => import('./management/scanner-page').then((m) => m.ScannerPage),
      },
      {
        path: 'etiquettes',
        canActivate: [labelsGuard],
        loadComponent: () => import('./management/labels-page').then((m) => m.LabelsPage),
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
