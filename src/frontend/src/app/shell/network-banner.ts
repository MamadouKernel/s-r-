import { Component, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { SyncService } from '../services/sync.service';

/** Bandeau réseau (DESIGN.md §4) : toujours un libellé, jamais une simple icône ; annoncé aux technologies d'assistance. */
@Component({
  selector: 'app-network-banner',
  imports: [RouterLink],
  template: `
    <p
      role="status"
      aria-live="polite"
      class="border-b px-4 py-2 text-sm font-semibold"
      [class.border-sauge]="banner().tone === 'ok'"
      [class.text-charbon]="banner().tone === 'ok' || banner().tone === 'info'"
      [class.border-information]="banner().tone === 'info'"
      [class.border-avertissement]="banner().tone === 'warning'"
      [class.text-avertissement]="banner().tone === 'warning'"
      [class.border-erreur]="banner().tone === 'error'"
      [class.text-erreur]="banner().tone === 'error'"
    >
      <span class="mr-2 inline-block h-2 w-2 rounded-full bg-current align-middle" aria-hidden="true"></span>
      {{ banner().text }}
      @if (sync.state() === 'auth') {
        <a routerLink="/connexion" class="ml-3 underline">Se reconnecter</a>
      }
    </p>
  `,
})
export class NetworkBanner {
  protected readonly sync = inject(SyncService);
  protected readonly banner = this.sync.banner;
}
