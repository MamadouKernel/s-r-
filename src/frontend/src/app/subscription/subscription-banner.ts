import { Component, computed, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { AuthService } from '../services/auth.service';
import { bannerFor } from './banner';
import { SubscriptionService } from './subscription.service';

/** Le bandeau d'abonnement, en haut de tous les écrans quand il y a quelque chose à dire (docs/specs/abonnement ABO-09). */
@Component({
  selector: 'app-subscription-banner',
  imports: [RouterLink],
  template: `
    @if (banner(); as current) {
      <div role="status" aria-live="polite" class="flex flex-wrap items-center gap-x-4 gap-y-2 border-b px-4 py-2 text-base" [class]="classes()">
        <span class="min-w-0 flex-1 font-semibold">{{ current.text }}</span>
        @if (current.action; as action) {
          <a class="btn" [routerLink]="action.link">{{ action.label }}</a>
        }
      </div>
    }
  `,
})
export class SubscriptionBanner {
  private readonly auth = inject(AuthService);
  private readonly subscription = inject(SubscriptionService);

  protected readonly classes = computed(() => {
    switch (this.banner()?.tone) {
      case 'error':
        return 'border-erreur bg-erreur/10';
      case 'warn':
        return 'border-avertissement bg-avertissement/10';
      default:
        return 'border-information bg-information/10';
    }
  });

  protected readonly banner = computed(() => (this.auth.signedIn() ? bannerFor(this.subscription.view(), this.auth.user()?.role) : null));
}
