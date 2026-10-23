import { Component, computed, inject } from '@angular/core';
import { Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { NetworkBanner } from './shell/network-banner';
import { canManageStock } from './management/roles';
import { AuthService } from './services/auth.service';
import { CashService } from './services/cash.service';
import { SyncService } from './services/sync.service';

@Component({
  imports: [RouterOutlet, RouterLink, RouterLinkActive, NetworkBanner],
  selector: 'app-root',
  templateUrl: './app.html',
})
export class App {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly cash = inject(CashService);
  protected readonly sync = inject(SyncService);

  protected readonly user = this.auth.user;
  protected readonly hasSession = computed(() => this.cash.session() !== null);
  protected readonly showManagement = computed(() => this.auth.signedIn() && canManageStock(this.auth.user()?.role));
  protected readonly showMenu = computed(() => this.auth.device() !== null && (this.auth.signedIn() || this.hasSession()));

  protected async logout(): Promise<void> {
    await this.auth.logout();
    await this.router.navigateByUrl('/connexion');
  }
}
