import { Component, computed, inject, OnDestroy, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { AuthService } from '../services/auth.service';
import { NetworkService } from '../services/network.service';
import { ApiError } from '../sync/sync-api';
import { OnboardingApi, SignupStarted } from './onboarding-api';
import { ACTIVITIES, looksLikeEmail, onboardingError, PASSWORD_RULE, TERMS_VERSION } from './wording';

type Step = 'shop' | 'verify';

/**
 * Inscription en deux écrans (INS-10) : la boutique, puis le code reçu par e-mail avec le mot de passe. L'identifiant de la demande reste dans
 * l'application : le commerçant ne tape que son nom, son adresse, le code à 6 chiffres et son mot de passe.
 */
@Component({
  selector: 'app-signup-page',
  imports: [RouterLink],
  template: `
    <section class="mx-auto max-w-md panel" aria-labelledby="inscription-titre">
      <p class="mb-1 text-sm font-semibold text-ardoise" aria-live="polite">Étape {{ step() === 'shop' ? 1 : 2 }} sur 2</p>
      <h2 id="inscription-titre" class="mb-4 text-xl font-bold">{{ step() === 'shop' ? 'Créer ma boutique' : 'Vérifier mon adresse e-mail' }}</h2>

      @if (!network.online()) {
        <p class="mb-4 font-semibold text-avertissement" role="note">Vous êtes hors ligne : l’inscription a besoin d’internet.</p>
      }

      @if (step() === 'shop') {
        <form (submit)="submitShop($event)" novalidate>
          <label class="field-label" for="nom-boutique">Nom de votre boutique</label>
          <input id="nom-boutique" class="field" type="text" autocomplete="organization" [value]="commerceName()" (input)="commerceName.set($any($event.target).value)" [attr.aria-invalid]="errors().commerceName ? 'true' : null" aria-describedby="nom-boutique-erreur" />
          <p id="nom-boutique-erreur" class="field-error">{{ errors().commerceName }}</p>

          <label class="field-label mt-4" for="activite">Votre activité</label>
          <select id="activite" class="field" (change)="activity.set($any($event.target).value)">
            @for (option of activities; track option.value) {
              <option [value]="option.value" [selected]="activity() === option.value">{{ option.label }}</option>
            }
          </select>

          <label class="field-label mt-4" for="nom-proprietaire">Votre nom</label>
          <input id="nom-proprietaire" class="field" type="text" autocomplete="name" [value]="ownerName()" (input)="ownerName.set($any($event.target).value)" [attr.aria-invalid]="errors().ownerName ? 'true' : null" aria-describedby="nom-proprietaire-erreur" />
          <p id="nom-proprietaire-erreur" class="field-error">{{ errors().ownerName }}</p>

          <label class="field-label mt-4" for="adresse-email">Votre adresse e-mail</label>
          <input id="adresse-email" class="field" type="email" inputmode="email" autocomplete="email" autocapitalize="off" spellcheck="false" [value]="email()" (input)="email.set($any($event.target).value)" [attr.aria-invalid]="errors().email ? 'true' : null" aria-describedby="adresse-email-aide adresse-email-erreur" />
          <p id="adresse-email-aide" class="field-help">Nous y envoyons un code pour vérifier que c’est bien vous. C’est aussi avec elle que vous vous connecterez.</p>
          <p id="adresse-email-erreur" class="field-error">{{ errors().email }}</p>

          <label class="mt-4 flex items-start gap-2 text-base">
            <input type="checkbox" class="mt-1 h-6 w-6 shrink-0" [checked]="accepted()" (change)="accepted.set($any($event.target).checked)" aria-describedby="conditions-erreur" />
            <span>J’accepte les <a class="font-semibold text-feuille underline" routerLink="/conditions" target="_blank">conditions d’utilisation</a>.</span>
          </label>
          <p id="conditions-erreur" class="field-error">{{ errors().terms }}</p>

          @if (error()) {
            <p class="field-error mt-3" role="alert">{{ error() }}</p>
          }
          <button type="submit" class="btn-primary mt-4 w-full" [disabled]="busy()">{{ busy() ? 'Envoi du code…' : 'Recevoir mon code' }}</button>
        </form>
        <p class="mt-4 text-center text-base">Vous avez déjà une boutique ? <a class="font-semibold text-feuille underline" routerLink="/connexion-proprietaire">Se connecter</a></p>
      } @else {
        <form (submit)="submitVerify($event)" novalidate>
          <p class="mb-4 text-base">Nous avons envoyé un code à 6 chiffres à <strong class="break-all">{{ email().trim() }}</strong>. Il est valable 10 minutes. Pensez à regarder dans vos courriers indésirables.</p>

          <label class="field-label" for="code">Code reçu par e-mail</label>
          <input id="code" class="field amount tracking-widest" type="text" inputmode="numeric" autocomplete="one-time-code" maxlength="6" [value]="code()" (input)="code.set($any($event.target).value)" [attr.aria-invalid]="errors().code ? 'true' : null" aria-describedby="code-erreur" />
          <p id="code-erreur" class="field-error" role="alert">{{ errors().code }}</p>

          <label class="field-label mt-4" for="mot-de-passe">Choisissez votre mot de passe</label>
          <input id="mot-de-passe" class="field" [type]="showPassword() ? 'text' : 'password'" autocomplete="new-password" [value]="password()" (input)="password.set($any($event.target).value)" [attr.aria-invalid]="errors().password ? 'true' : null" aria-describedby="mot-de-passe-aide mot-de-passe-erreur" />
          <p id="mot-de-passe-aide" class="field-help">{{ rule }}</p>
          <p id="mot-de-passe-erreur" class="field-error">{{ errors().password }}</p>
          <label class="mt-2 flex items-center gap-2 text-base">
            <input type="checkbox" class="h-6 w-6" [checked]="showPassword()" (change)="showPassword.set($any($event.target).checked)" />
            Afficher le mot de passe
          </label>

          @if (notice()) {
            <p class="mt-3 font-semibold text-succes" role="status">{{ notice() }}</p>
          }
          @if (error()) {
            <p class="field-error mt-3" role="alert">{{ error() }}</p>
          }
          <button type="submit" class="btn-primary mt-4 w-full" [disabled]="busy()">{{ busy() ? 'Création…' : 'Créer ma boutique' }}</button>
        </form>

        <div class="mt-4 flex flex-wrap items-center gap-3">
          <button type="button" class="btn" (click)="resend()" [disabled]="busy() || waitSeconds() > 0">Renvoyer le code</button>
          @if (waitSeconds() > 0) {
            <span class="text-sm text-ardoise">Possible dans {{ waitSeconds() }} s</span>
          }
          <button type="button" class="btn" (click)="back()">Changer d’adresse e-mail</button>
        </div>
      }
    </section>
  `,
})
export class SignupPage implements OnDestroy {
  private readonly api = inject(OnboardingApi);
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  protected readonly network = inject(NetworkService);

  protected readonly activities = ACTIVITIES;
  protected readonly rule = PASSWORD_RULE;

  protected readonly step = signal<Step>('shop');
  protected readonly commerceName = signal('');
  protected readonly activity = signal(ACTIVITIES[0].value);
  protected readonly ownerName = signal('');
  protected readonly email = signal('');
  protected readonly accepted = signal(false);
  protected readonly code = signal('');
  protected readonly password = signal('');
  protected readonly showPassword = signal(false);

  protected readonly errors = signal<Partial<Record<'commerceName' | 'ownerName' | 'email' | 'terms' | 'code' | 'password', string>>>({});
  protected readonly error = signal('');
  protected readonly notice = signal('');
  protected readonly busy = signal(false);

  private readonly verification = signal<SignupStarted | null>(null);
  private readonly now = signal(Date.now());
  private readonly timer = setInterval(() => this.now.set(Date.now()), 1000);

  /** Secondes à attendre avant de pouvoir demander un nouveau code (le serveur impose 60 s entre deux envois). */
  protected readonly waitSeconds = computed(() => {
    const at = this.verification()?.resendAvailableAt;
    return at ? Math.max(Math.ceil((Date.parse(at) - this.now()) / 1000), 0) : 0;
  });

  ngOnDestroy(): void {
    clearInterval(this.timer);
  }

  protected async submitShop(event: Event): Promise<void> {
    event.preventDefault();
    if (this.busy()) {
      return;
    }
    const errors: ReturnType<typeof this.errors> = {};
    if (this.commerceName().trim().length < 2) {
      errors.commerceName = 'Donnez le nom de votre boutique (2 lettres au moins).';
    }
    if (this.ownerName().trim().length < 2) {
      errors.ownerName = 'Donnez votre nom (2 lettres au moins).';
    }
    if (!looksLikeEmail(this.email())) {
      errors.email = 'Cette adresse e-mail n’est pas valide. Exemple : nom@exemple.ci';
    }
    if (!this.accepted()) {
      errors.terms = 'Cochez la case pour continuer.';
    }
    this.errors.set(errors);
    this.error.set('');
    if (Object.keys(errors).length > 0) {
      return;
    }

    this.busy.set(true);
    try {
      this.verification.set(
        await this.api.startSignup({
          email: this.email().trim(),
          commerceName: this.commerceName().trim(),
          activity: this.activity(),
          ownerName: this.ownerName().trim(),
          termsVersion: TERMS_VERSION,
        }),
      );
      this.now.set(Date.now());
      this.code.set('');
      this.password.set('');
      this.notice.set('');
      this.step.set('verify');
    } catch (error) {
      this.error.set(onboardingError(error));
    } finally {
      this.busy.set(false);
    }
  }

  protected async submitVerify(event: Event): Promise<void> {
    event.preventDefault();
    const verification = this.verification();
    if (this.busy() || !verification) {
      return;
    }
    const errors: ReturnType<typeof this.errors> = {};
    const code = this.code().replace(/\s/g, '');
    if (!/^\d{6}$/.test(code)) {
      errors.code = 'Saisissez les 6 chiffres reçus par e-mail.';
    }
    const password = this.password();
    if (password.length < 10 || !/\p{L}/u.test(password) || !/\d/.test(password)) {
      errors.password = PASSWORD_RULE;
    }
    this.errors.set(errors);
    this.error.set('');
    this.notice.set('');
    if (Object.keys(errors).length > 0) {
      return;
    }

    this.busy.set(true);
    try {
      const session = await this.api.completeSignup(verification.verificationId, code, password);
      await this.auth.acceptOwnerSession(session);
      this.password.set('');
      await this.router.navigateByUrl('/tableau-de-bord');
    } catch (error) {
      this.handleCompletionError(error);
    } finally {
      this.busy.set(false);
    }
  }

  protected async resend(): Promise<void> {
    const verification = this.verification();
    if (this.busy() || !verification || this.waitSeconds() > 0) {
      return;
    }
    this.error.set('');
    this.busy.set(true);
    try {
      this.verification.set(await this.api.resendCode(verification.verificationId));
      this.now.set(Date.now());
      this.code.set('');
      this.errors.set({});
      this.notice.set('Un nouveau code vient d’être envoyé. L’ancien ne marche plus.');
    } catch (error) {
      this.error.set(onboardingError(error));
    } finally {
      this.busy.set(false);
    }
  }

  protected back(): void {
    this.step.set('shop');
    this.verification.set(null);
    this.errors.set({});
    this.error.set('');
    this.notice.set('');
  }

  private handleCompletionError(error: unknown): void {
    const message = onboardingError(error);
    if (error instanceof ApiError && error.code === 'invalid_code') {
      this.errors.set({ code: message });
      this.code.set('');
    } else if (error instanceof ApiError && error.code === 'verification_expired') {
      this.error.set(`${message} Appuyez sur « Changer d’adresse e-mail » pour recommencer.`);
    } else if (error instanceof ApiError && error.code === 'rule_violation') {
      this.errors.set({ password: message });
    } else {
      this.error.set(message);
    }
  }
}
