import { Component, inject, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { FlashService } from '../management/flash.service';
import { ApiError } from '../sync/sync-api';
import { AuthService, loginErrorMessage, OfflineLoginError } from '../services/auth.service';
import { landingPath } from './landing';
import { CashService } from '../services/cash.service';
import { CatalogueService } from '../services/catalogue.service';
import { SyncService } from '../services/sync.service';

/** S2 : identifiant et code PIN (PWA-01). Le PIN n'est jamais conservé. */
@Component({
  selector: 'app-login-page',
  imports: [RouterLink],
  template: `
    <section class="mx-auto max-w-md panel">
      <h2 class="mb-4 text-xl font-bold">Connexion</h2>
      @if (notice()) {
        <p class="mb-4 font-semibold text-succes" role="status">{{ notice() }}</p>
      }
      <form (submit)="submit($event)" novalidate>
        <label class="field-label" for="identifiant">Identifiant</label>
        <input
          id="identifiant"
          class="field"
          type="text"
          autocomplete="username"
          autocapitalize="off"
          spellcheck="false"
          [value]="identifier()"
          (input)="identifier.set($any($event.target).value)"
        />

        <label class="field-label mt-4" for="pin">Code PIN</label>
        <input
          id="pin"
          class="field amount"
          type="password"
          inputmode="numeric"
          autocomplete="off"
          maxlength="12"
          [value]="pin()"
          (input)="pin.set($any($event.target).value)"
          [attr.aria-invalid]="error() ? 'true' : null"
          aria-describedby="connexion-erreur"
        />
        <p id="connexion-erreur" class="field-error" role="alert">{{ error() }}</p>

        <button type="submit" class="btn-primary mt-4 w-full" [disabled]="busy()">
          {{ busy() ? 'Connexion…' : 'Se connecter' }}
        </button>
      </form>
      @if (noNetwork()) {
        <div class="mt-4 rounded border border-sauge bg-ivoire p-3" role="status">
          <p class="font-semibold">Pas de réseau. Vous pouvez ouvrir la caisse sans réseau avec votre code.</p>
          <p class="field-help">Seuls les comptes déjà connectés avec le réseau sur ce téléphone, ces trois derniers jours, peuvent entrer.</p>
          <button type="button" class="btn mt-2 w-full" [disabled]="busy()" (click)="openWithoutNetwork()">Ouvrir la caisse sans réseau</button>
        </div>
      }
      <p class="mt-4 text-center text-base">Vous êtes le propriétaire ? <a class="font-semibold text-feuille underline" routerLink="/connexion-proprietaire">Connexion du propriétaire</a></p>
    </section>
  `,
})
export class LoginPage {
  private readonly auth = inject(AuthService);
  private readonly cash = inject(CashService);
  private readonly catalogue = inject(CatalogueService);
  private readonly sync = inject(SyncService);
  private readonly router = inject(Router);

  protected readonly notice = signal(inject(FlashService).consume());
  protected readonly identifier = signal('');
  protected readonly pin = signal('');
  protected readonly error = signal('');
  protected readonly busy = signal(false);
  /** Le serveur n'a pas répondu : la connexion sans réseau est proposée (docs/specs/connexion-hors-ligne HLC-02). */
  protected readonly noNetwork = signal(false);

  protected async submit(event: Event): Promise<void> {
    event.preventDefault();
    if (this.busy()) {
      return;
    }
    if (!this.identifier().trim() || !this.pin()) {
      this.error.set('Saisissez votre identifiant et votre code PIN.');
      return;
    }
    this.error.set('');
    this.noNetwork.set(false);
    this.busy.set(true);
    try {
      await this.auth.login(this.identifier(), this.pin());
      this.pin.set('');
      // Catalogue frais et envoi de ce qui attendait (jeton expiré pendant une coupure) ; sans bloquer l'accès à la caisse.
      void this.catalogue.refresh();
      void this.sync.requestFlush();
      await this.router.navigateByUrl(landingPath(this.auth.user()?.role, this.cash.session() !== null));
    } catch (error) {
      if (error instanceof ApiError && error.status === 0) {
        // Pas de réponse du serveur : le code tapé reste dans le champ le temps de choisir « sans réseau » ; il n'est écrit nulle part.
        this.noNetwork.set(true);
        return;
      }
      this.pin.set('');
      this.error.set(loginErrorMessage(error));
      // Le serveur ne reconnaît plus cet appareil : on l'oublie, les ventes déjà enregistrées restent dans le journal.
      if (error instanceof ApiError && error.code === 'device_not_authorized' && this.auth.device()?.deviceId) {
        await this.auth.markDeviceRefused();
        await this.router.navigateByUrl('/bienvenue');
      }
    } finally {
      this.busy.set(false);
    }
  }

  protected async openWithoutNetwork(): Promise<void> {
    if (this.busy()) {
      return;
    }
    this.error.set('');
    this.busy.set(true);
    try {
      await this.auth.loginOffline(this.identifier(), this.pin());
      this.pin.set('');
      await this.router.navigateByUrl(landingPath(this.auth.user()?.role, this.cash.session() !== null));
    } catch (error) {
      this.pin.set('');
      this.error.set(error instanceof OfflineLoginError ? error.message : 'La connexion a échoué. Réessayez.');
    } finally {
      this.busy.set(false);
    }
  }
}
