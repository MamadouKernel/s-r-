import { Component, computed, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { AuthService } from '../services/auth.service';

/** Premier écran d'un appareil qui ne connaît pas encore de boutique : trois choix, en mots simples. */
@Component({
  selector: 'app-welcome-page',
  imports: [RouterLink],
  template: `
    <section class="mx-auto max-w-md" aria-labelledby="bienvenue-titre">
      <h2 id="bienvenue-titre" class="mb-2 text-2xl font-bold">Bienvenue sur Sôrô</h2>
      <p class="mb-4 text-base text-ardoise">La caisse simple pour votre commerce. Que voulez-vous faire ?</p>

      @if (removed()) {
        <div class="panel mb-4" role="alert">
          <p class="font-semibold text-erreur">Cet appareil n’est plus autorisé à utiliser la caisse.</p>
          <p class="mt-1 text-base">
            Demandez un nouveau lien au propriétaire ou au gérant. Les ventes déjà enregistrées sur cet appareil sont conservées : elles partiront
            quand il sera de nouveau autorisé.
          </p>
        </div>
      }

      <div class="flex flex-col gap-3">
        <a class="btn-primary w-full" routerLink="/inscription">Créer ma boutique</a>
        <a class="btn w-full" routerLink="/connexion-proprietaire">J’ai déjà une boutique</a>
      </div>

      <div class="panel mt-6">
        <h3 class="mb-1 text-lg font-bold">J’ai reçu un lien</h3>
        <p class="text-base">
          Votre propriétaire ou votre gérant vous a envoyé un lien, ou un QR code à scanner avec l’appareil photo. Ouvrez-le simplement sur ce
          téléphone : la caisse se configure toute seule.
        </p>
      </div>
    </section>
  `,
})
export class WelcomePage {
  private readonly auth = inject(AuthService);
  protected readonly removed = computed(() => this.auth.device()?.revoked === true);
}
