import { Component, effect, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { DRAWER_WARNING_FCFA } from '../core/cash-count';
import { CashService } from '../services/cash.service';
import { SyncService } from '../services/sync.service';

/**
 * « Trop d'argent dans le tiroir » (docs/specs/cloture-sorties-rapport CLO-13) : au-dessus du seuil, la caisse invite à faire un versement au coffre.
 * Elle ne dit pas combien le tiroir doit contenir (clôture à l'aveugle) et ne bloque rien.
 */
@Component({
  selector: 'app-drawer-warning',
  imports: [RouterLink],
  template: `
    @if (high()) {
      <p class="border-b border-avertissement px-4 py-2 text-sm font-semibold text-avertissement" role="status">
        Il y a beaucoup d’argent dans le tiroir : faites un versement au coffre.
        <a routerLink="/mouvements" class="ml-3 underline">Mouvements de caisse</a>
      </p>
    }
  `,
})
export class DrawerWarning {
  private readonly cash = inject(CashService);
  private readonly sync = inject(SyncService);
  protected readonly high = signal(false);

  constructor() {
    // Une vente, un mouvement ou un envoi change les compteurs : on relit l'état du tiroir à chaque fois.
    effect(() => {
      this.sync.counts();
      const open = this.cash.session() !== null;
      void this.refresh(open);
    });
  }

  private async refresh(open: boolean): Promise<void> {
    this.high.set(open && ((await this.cash.summary())?.expectedCashFcfa ?? 0) > DRAWER_WARNING_FCFA);
  }
}
