import { Component, inject, signal } from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { FlashService } from '../management/flash.service';
import { AuthService } from '../services/auth.service';
import { onboardingError } from './wording';

/**
 * Le lien du propriétaire ou du gérant (INS-09). Le jeton se lit dans la partie « # » de l'adresse, que le navigateur n'envoie jamais à un
 * serveur ; il n'est ni affiché ni conservé. L'utilisateur ne donne que le nom de l'appareil.
 */
@Component({
  selector: 'app-join-page',
  imports: [RouterLink],
  template: `
    <section class="mx-auto max-w-md panel" aria-labelledby="rejoindre-titre">
      <h2 id="rejoindre-titre" class="mb-2 text-xl font-bold">Rejoindre la boutique</h2>
      @if (!token()) {
        <p class="text-base" role="alert">
          Ce lien est incomplet. Ouvrez-le exactement comme vous l’avez reçu, ou scannez de nouveau le QR code. Sinon, demandez un nouveau lien au
          propriétaire ou au gérant.
        </p>
        <a class="btn mt-4 w-full" routerLink="/bienvenue">Retour</a>
      } @else {
        <form (submit)="submit($event)" novalidate>
          <p class="mb-4 text-base">Cet appareil va servir de caisse pour la boutique qui vous a invité.</p>
          <label class="field-label" for="nom-appareil">Nom de cet appareil (facultatif)</label>
          <input id="nom-appareil" class="field" type="text" autocomplete="off" placeholder="Caisse" [value]="name()" (input)="name.set($any($event.target).value)" [attr.aria-invalid]="error() ? 'true' : null" aria-describedby="nom-appareil-aide rejoindre-erreur" />
          <p id="nom-appareil-aide" class="field-help">Par exemple : Caisse 1, Caisse du fond. Laissez vide pour garder le nom prévu par votre gérant.</p>
          <p id="rejoindre-erreur" class="field-error" role="alert">{{ error() }}</p>
          <button type="submit" class="btn-primary mt-4 w-full" [disabled]="busy()">{{ busy() ? 'Configuration…' : 'Configurer cet appareil' }}</button>
        </form>
      }
    </section>
  `,
})
export class JoinPage {
  private readonly auth = inject(AuthService);
  private readonly flash = inject(FlashService);
  private readonly router = inject(Router);

  protected readonly token = signal(inject(ActivatedRoute).snapshot.fragment ?? '');
  /** Vide par défaut : le serveur garde alors le nom que le gérant a prévu dans l'invitation, sinon « Caisse ». */
  protected readonly name = signal('');
  protected readonly error = signal('');
  protected readonly busy = signal(false);

  protected async submit(event: Event): Promise<void> {
    event.preventDefault();
    if (this.busy()) {
      return;
    }
    this.error.set('');
    this.busy.set(true);
    try {
      const device = await this.auth.enrollDevice(this.token(), this.name());
      this.token.set('');
      this.flash.show(`Cet appareil est prêt pour « ${device.commerceName} ». Connectez-vous avec votre identifiant et votre code PIN.`);
      await this.router.navigateByUrl('/connexion');
    } catch (error) {
      this.error.set(onboardingError(error));
    } finally {
      this.busy.set(false);
    }
  }
}
