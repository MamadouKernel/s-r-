import { Component, computed, inject } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { AuthService } from '../services/auth.service';
import { canManageTeam } from './roles';

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
    </nav>
    <router-outlet />
  `,
})
export class ManagementShell {
  private readonly auth = inject(AuthService);
  protected readonly showTeam = computed(() => canManageTeam(this.auth.user()?.role));
}
