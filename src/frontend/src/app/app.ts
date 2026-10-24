import { Component, computed, inject } from '@angular/core';
import { Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { NetworkBanner } from './shell/network-banner';
import { canManageStock, canReadAuditLog, canViewReports } from './management/roles';
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
  protected readonly showDashboard = computed(() => this.auth.signedIn() && canViewReports(this.auth.user()?.role));
  protected readonly showJournal = computed(() => this.auth.signedIn() && canReadAuditLog(this.auth.user()?.role));
  protected readonly showManagement = computed(() => this.auth.signedIn() && canManageStock(this.auth.user()?.role));
  protected readonly showMenu = computed(() => this.auth.device() !== null && (this.auth.signedIn() || this.hasSession()));

  protected async logout(): Promise<void> {
    const role = this.auth.user()?.role;
    await this.auth.logout();
    await this.router.navigateByUrl(role === 'Owner' ? '/connexion-proprietaire' : '/connexion');
  }
}
