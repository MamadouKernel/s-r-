import { Component, inject, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { CustomerService } from '../customers/customer.service';
import { FlashService } from '../management/flash.service';
import { landingPath } from '../pages/landing';
import { AuthService } from '../services/auth.service';
import { CashService } from '../services/cash.service';
import { CatalogueService } from '../services/catalogue.service';
import { SyncService } from '../services/sync.service';
import { OnboardingApi } from './onboarding-api';
import { looksLikeEmail, onboardingError } from './wording';

/** Connexion du propriétaire : adresse e-mail et mot de passe (INS-05). Aucun code à chaque connexion (ADR 0005). */
@Component({
  selector: 'app-owner-login-page',
  imports: [RouterLink],
  template: `
    <section class="mx-auto max-w-md panel" aria-labelledby="proprietaire-titre">
      <h2 id="proprietaire-titre" class="mb-4 text-xl font-bold">Connexion du propriétaire</h2>
      @if (notice()) {
        <p class="mb-4 font-semibold text-succes" role="status">{{ notice() }}</p>
      }
      <form (submit)="submit($event)" novalidate>
        <label class="field-label" for="email-proprietaire">Votre adresse e-mail</label>
        <input id="email-proprietaire" class="field" type="email" inputmode="email" autocomplete="username" autocapitalize="off" spellcheck="false" [value]="email()" (input)="email.set($any($event.target).value)" />

        <label class="field-label mt-4" for="mdp-proprietaire">Mot de passe</label>
        <input id="mdp-proprietaire" class="field" [type]="showPassword() ? 'text' : 'password'" autocomplete="current-password" [value]="password()" (input)="password.set($any($event.target).value)" [attr.aria-invalid]="error() ? 'true' : null" aria-describedby="connexion-erreur" />
        <label class="mt-2 flex items-center gap-2 text-base">
          <input type="checkbox" class="h-6 w-6" [checked]="showPassword()" (change)="showPassword.set($any($event.target).checked)" />
          Afficher le mot de passe
        </label>
        <p id="connexion-erreur" class="field-error" role="alert">{{ error() }}</p>

        <button type="submit" class="btn-primary mt-4 w-full" [disabled]="busy()">{{ busy() ? 'Connexion…' : 'Se connecter' }}</button>
      </form>
      <p class="mt-4 text-center text-base"><a class="font-semibold text-feuille underline" routerLink="/mot-de-passe-oublie">Mot de passe oublié ?</a></p>
      <p class="mt-2 text-center text-base">Pas encore de boutique ? <a class="font-semibold text-feuille underline" routerLink="/inscription">Créer ma boutique</a></p>
    </section>
  `,
})
export class OwnerLoginPage {
  private readonly api = inject(OnboardingApi);
  private readonly auth = inject(AuthService);
  private readonly cash = inject(CashService);
  private readonly catalogue = inject(CatalogueService);
  private readonly customers = inject(CustomerService);
  private readonly sync = inject(SyncService);
  private readonly router = inject(Router);

  protected readonly notice = signal(inject(FlashService).consume());
  protected readonly email = signal('');
  protected readonly password = signal('');
  protected readonly showPassword = signal(false);
  protected readonly error = signal('');
  protected readonly busy = signal(false);

  protected async submit(event: Event): Promise<void> {
    event.preventDefault();
    if (this.busy()) {
      return;
    }
    if (!looksLikeEmail(this.email()) || !this.password()) {
      this.error.set('Saisissez votre adresse e-mail et votre mot de passe.');
      return;
    }
    this.error.set('');
    this.notice.set('');
    this.busy.set(true);
    try {
      const session = await this.api.ownerLogin(this.email().trim(), this.password());
      await this.auth.acceptOwnerSession(session);
      this.password.set('');
      // Le propriétaire peut aussi vendre depuis ce navigateur : catalogue et clients frais, et envoi de ce qui attendait.
      void this.catalogue.refresh();
      void this.customers.refresh();
      void this.sync.requestFlush();
      await this.router.navigateByUrl(landingPath(this.auth.user()?.role, this.cash.session() !== null));
    } catch (error) {
      this.password.set('');
      this.error.set(onboardingError(error));
    } finally {
      this.busy.set(false);
    }
  }
}
