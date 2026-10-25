import { Component, computed, inject } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { AuthService } from '../services/auth.service';
import { canExportData, canManageDevices, canManageTeam } from './roles';

/** Espace « Gestion » : un titre, une navigation entre ses sections et la section courante. */
@Component({
  selector: 'app-management-shell',
  imports: [RouterOutlet, RouterLink, RouterLinkActive],
  template: `
    <nav aria-label="Sections de la gestion" class="mb-4 flex flex-wrap gap-2 border-b border-sauge pb-3">
      <a class="btn" routerLink="/gestion/produits" routerLinkActive="border-feuille text-feuille" ariaCurrentWhenActive="page">Produits et stock</a>
      @if (showTeam()) {
        <a class="btn" routerLink="/gestion/equipe" routerLinkActive="border-feuille text-feuille" ariaCurrentWhenActive="page">Équipe</a>
      }
      @if (showTeam()) {
        <a class="btn" routerLink="/gestion/abonnement" routerLinkActive="border-feuille text-feuille" ariaCurrentWhenActive="page">Mon abonnement</a>
      }
      @if (showExport()) {
        <a class="btn" routerLink="/gestion/export" routerLinkActive="border-feuille text-feuille" ariaCurrentWhenActive="page">Exporter mes données</a>
      }
      @if (showDevices()) {
        <a class="btn" routerLink="/gestion/appareils" routerLinkActive="border-feuille text-feuille" ariaCurrentWhenActive="page">Appareils</a>
      }
    </nav>
    <router-outlet />
  `,
})
export class ManagementShell {
  private readonly auth = inject(AuthService);
  protected readonly showTeam = computed(() => canManageTeam(this.auth.user()?.role));
  protected readonly showDevices = computed(() => canManageDevices(this.auth.user()?.role));
  protected readonly showExport = computed(() => canExportData(this.auth.user()?.role));
}
