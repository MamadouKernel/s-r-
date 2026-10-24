import { Component } from '@angular/core';
import { RouterLink } from '@angular/router';

/**
 * Texte provisoire : il doit être relu et validé (juridique) avant l'ouverture au public. La version acceptée à l'inscription est celle de
 * `TERMS_VERSION`, qui devra changer avec le texte définitif.
 */
@Component({
  selector: 'app-conditions-page',
  imports: [RouterLink],
  template: `
    <section class="mx-auto max-w-2xl panel" aria-labelledby="conditions-titre">
      <h2 id="conditions-titre" class="mb-2 text-xl font-bold">Conditions d’utilisation</h2>
      <p class="mb-4 font-semibold text-avertissement" role="note">Version provisoire, en attente de validation avant l’ouverture au public.</p>
      <ul class="list-disc space-y-2 pl-6 text-base">
        <li>Vous avez 30 jours d’essai gratuit à partir de la création de votre boutique.</li>
        <li>Les données de votre boutique (produits, ventes, équipe) sont les vôtres et ne sont visibles par aucune autre boutique.</li>
        <li>Votre adresse e-mail sert à vous identifier et à vous envoyer les codes de vérification, rien d’autre.</li>
        <li>Vous protégez votre mot de passe et vous retirez les appareils que vous ne voulez plus voir se connecter.</li>
      </ul>
      <a class="btn mt-6" routerLink="/bienvenue">Retour</a>
    </section>
  `,
})
export class ConditionsPage {}
