import { Component, inject, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { FlashService } from '../management/flash.service';
import { OnboardingApi } from './onboarding-api';
import { looksLikeEmail, onboardingError, PASSWORD_RULE } from './wording';

/** Mot de passe oublié (INS-06) : l'adresse, puis le code reçu et le nouveau mot de passe. La réponse ne dit jamais si l'adresse a un compte. */
@Component({
  selector: 'app-password-reset-page',
  imports: [RouterLink],
  template: `
    <section class="mx-auto max-w-md panel" aria-labelledby="reinit-titre">
      <h2 id="reinit-titre" class="mb-4 text-xl font-bold">Mot de passe oublié</h2>

      @if (!sent()) {
        <form (submit)="submitEmail($event)" novalidate>
          <p class="mb-4 text-base">Donnez l’adresse e-mail de votre boutique. Nous vous envoyons un code pour choisir un nouveau mot de passe.</p>
          <label class="field-label" for="reinit-email">Votre adresse e-mail</label>
          <input id="reinit-email" class="field" type="email" inputmode="email" autocomplete="email" autocapitalize="off" spellcheck="false" [value]="email()" (input)="email.set($any($event.target).value)" [attr.aria-invalid]="error() ? 'true' : null" aria-describedby="reinit-erreur" />
          <p id="reinit-erreur" class="field-error" role="alert">{{ error() }}</p>
          <button type="submit" class="btn-primary mt-4 w-full" [disabled]="busy()">{{ busy() ? 'Envoi…' : 'Recevoir un code' }}</button>
        </form>
      } @else {
        <form (submit)="submitNew($event)" novalidate>
          <p class="mb-4 text-base" role="status">
            Si cette adresse e-mail a un compte, un code à 6 chiffres vient d’être envoyé. Il est valable 10 minutes. Regardez aussi dans vos courriers indésirables.
          </p>
          <label class="field-label" for="reinit-code">Code reçu par e-mail</label>
          <input id="reinit-code" class="field amount tracking-widest" type="text" inputmode="numeric" autocomplete="one-time-code" maxlength="6" [value]="code()" (input)="code.set($any($event.target).value)" />

          <label class="field-label mt-4" for="reinit-mdp">Nouveau mot de passe</label>
          <input id="reinit-mdp" class="field" [type]="showPassword() ? 'text' : 'password'" autocomplete="new-password" [value]="password()" (input)="password.set($any($event.target).value)" aria-describedby="reinit-mdp-aide reinit-erreur" />
          <p id="reinit-mdp-aide" class="field-help">{{ rule }}</p>
          <label class="mt-2 flex items-center gap-2 text-base">
            <input type="checkbox" class="h-6 w-6" [checked]="showPassword()" (change)="showPassword.set($any($event.target).checked)" />
            Afficher le mot de passe
          </label>
          <p id="reinit-erreur" class="field-error" role="alert">{{ error() }}</p>
          <button type="submit" class="btn-primary mt-4 w-full" [disabled]="busy()">{{ busy() ? 'Enregistrement…' : 'Changer mon mot de passe' }}</button>
        </form>
        <button type="button" class="btn mt-4" (click)="again()">Changer d’adresse e-mail</button>
      }
      <p class="mt-4 text-center text-base"><a class="font-semibold text-feuille underline" routerLink="/connexion-proprietaire">Retour à la connexion</a></p>
    </section>
  `,
})
export class PasswordResetPage {
  private readonly api = inject(OnboardingApi);
  private readonly flash = inject(FlashService);
  private readonly router = inject(Router);

  protected readonly rule = PASSWORD_RULE;
  protected readonly email = signal('');
  protected readonly code = signal('');
  protected readonly password = signal('');
  protected readonly showPassword = signal(false);
  protected readonly sent = signal(false);
  protected readonly error = signal('');
  protected readonly busy = signal(false);

  protected async submitEmail(event: Event): Promise<void> {
    event.preventDefault();
    if (this.busy()) {
      return;
    }
    if (!looksLikeEmail(this.email())) {
      this.error.set('Cette adresse e-mail n’est pas valide. Exemple : nom@exemple.ci');
      return;
    }
    this.error.set('');
    this.busy.set(true);
    try {
      await this.api.startPasswordReset(this.email().trim());
      this.sent.set(true);
    } catch (error) {
      this.error.set(onboardingError(error));
    } finally {
      this.busy.set(false);
    }
  }

  protected async submitNew(event: Event): Promise<void> {
    event.preventDefault();
    if (this.busy()) {
      return;
    }
    const code = this.code().replace(/\s/g, '');
    if (!/^\d{6}$/.test(code)) {
      this.error.set('Saisissez les 6 chiffres reçus par e-mail.');
      return;
    }
    const password = this.password();
    if (password.length < 10 || !/\p{L}/u.test(password) || !/\d/.test(password)) {
      this.error.set(PASSWORD_RULE);
      return;
    }
    this.error.set('');
    this.busy.set(true);
    try {
      await this.api.confirmPasswordReset(this.email().trim(), code, password);
      this.flash.show('Mot de passe changé. Connectez-vous avec le nouveau.');
      await this.router.navigateByUrl('/connexion-proprietaire');
    } catch (error) {
      this.error.set(onboardingError(error));
    } finally {
      this.busy.set(false);
    }
  }

  protected again(): void {
    this.sent.set(false);
    this.code.set('');
    this.password.set('');
    this.error.set('');
  }
}
