import { Component, computed, inject, linkedSignal, signal } from '@angular/core';
import { AuthService, loginErrorMessage } from '../services/auth.service';
import { NetworkService } from '../services/network.service';
import { SyncService } from '../services/sync.service';

/**
 * Le réseau est revenu alors que la caisse a été ouverte sans réseau : le code est demandé à nouveau, en ligne, pour obtenir un jeton et
 * envoyer la file (docs/specs/connexion-hors-ligne HLC-08). Le code n'est gardé nulle part.
 */
@Component({
  selector: 'app-reconnect-prompt',
  template: `
    @if (visible()) {
      <form class="border-b border-avertissement bg-ivoire px-4 py-3" (submit)="submit($event)" novalidate aria-labelledby="reconnexion-titre">
        <p id="reconnexion-titre" class="font-semibold">Le réseau est revenu. Tapez votre code pour envoyer vos ventes.</p>
        <div class="mt-2 grid gap-2 sm:grid-cols-[1fr_1fr_auto]">
          <div>
            <label class="field-label" for="reconnexion-identifiant">Identifiant</label>
            <input id="reconnexion-identifiant" class="field" type="text" autocomplete="username" autocapitalize="off" spellcheck="false" [value]="identifier()" (input)="identifier.set($any($event.target).value)" />
          </div>
          <div>
            <label class="field-label" for="reconnexion-pin">Code PIN</label>
            <input id="reconnexion-pin" class="field amount" type="password" inputmode="numeric" autocomplete="off" maxlength="12" [value]="pin()" (input)="pin.set($any($event.target).value)" />
          </div>
          <button type="submit" class="btn-primary self-end" [disabled]="busy()">{{ busy() ? 'Envoi…' : 'Envoyer' }}</button>
        </div>
        <p class="field-error" role="alert">{{ error() }}</p>
      </form>
    }
  `,
})
export class ReconnectPrompt {
  private readonly auth = inject(AuthService);
  private readonly network = inject(NetworkService);
  private readonly sync = inject(SyncService);

  protected readonly visible = computed(() => this.network.online() && !this.auth.signedIn() && this.auth.offlineSession() !== null);
  /** Préremplie avec le compte entré sans réseau, même si l'entrée a lieu après l'ouverture de l'application. */
  protected readonly identifier = linkedSignal(() => this.auth.offlineSession()?.identifier ?? '');
  protected readonly pin = signal('');
  protected readonly error = signal('');
  protected readonly busy = signal(false);

  protected async submit(event: Event): Promise<void> {
    event.preventDefault();
    if (this.busy()) {
      return;
    }
    if (!this.pin()) {
      this.error.set('Tapez votre code pour envoyer vos ventes.');
      return;
    }
    this.error.set('');
    this.busy.set(true);
    try {
      await this.auth.login(this.identifier(), this.pin());
      await this.sync.requestFlush();
    } catch (error) {
      this.error.set(loginErrorMessage(error));
    } finally {
      this.pin.set('');
      this.busy.set(false);
    }
  }
}
