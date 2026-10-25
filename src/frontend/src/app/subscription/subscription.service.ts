import { computed, inject, Injectable, OnDestroy, signal } from '@angular/core';
import { SubscriptionApi, SubscriptionView } from './subscription-api';

/** L'état est relu chaque minute : un paiement validé par l'équipe libère la boutique sans reconnexion (docs/specs/abonnement ABO-09). */
const REFRESH_MS = 60_000;

/**
 * L'état d'abonnement de la boutique, partagé par le bandeau, l'écran « Mon abonnement » et la caisse. En cas d'échec de lecture (hors ligne), le
 * dernier état connu reste : une caisse hors ligne continue de vendre jusqu'à sa prochaine synchronisation, comme le serveur l'accepte.
 */
@Injectable({ providedIn: 'root' })
export class SubscriptionService implements OnDestroy {
  private readonly api = inject(SubscriptionApi);
  private readonly viewSignal = signal<SubscriptionView | null>(null);
  private timer: ReturnType<typeof setInterval> | null = null;

  readonly view = this.viewSignal.asReadonly();
  readonly readOnly = computed(() => this.viewSignal()?.isReadOnly === true);

  async refresh(): Promise<void> {
    try {
      this.viewSignal.set(await this.api.get());
    } catch {
      // Hors ligne ou serveur indisponible : on garde ce qu'on savait.
    }
  }

  /** Lit l'état maintenant puis chaque minute, tant que l'utilisateur est connecté. */
  start(): void {
    if (this.timer !== null) {
      return;
    }
    void this.refresh();
    this.timer = setInterval(() => void this.refresh(), REFRESH_MS);
  }

  stop(): void {
    if (this.timer !== null) {
      clearInterval(this.timer);
      this.timer = null;
    }
    this.viewSignal.set(null);
  }

  ngOnDestroy(): void {
    this.stop();
  }
}
