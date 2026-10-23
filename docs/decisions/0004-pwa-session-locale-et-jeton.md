# 0004 — PWA : état local, jeton et appareil

**Statut :** provisoire (à valider avec le pilote et la revue sécurité).

## Contexte

La caisse doit continuer à vendre pendant une coupure, y compris après un rechargement de la page ou un redémarrage de l'appareil. Le
serveur émet un JWT d'une durée de 8 heures (ADR 0003). La question du PIN hors ligne et de la durée d'accès local est ouverte
(`docs/architecture.md`).

## Décision provisoire

- Le **journal des opérations** (ventes, ouverture, clôture), le **catalogue**, le **contexte d'appareil** (code du commerce, identifiants de
  caisse et de point de vente) et la **séquence locale** vivent dans IndexedDB : ils survivent au rechargement.
- Le **jeton d'accès** est conservé dans IndexedDB avec son échéance, pour pouvoir reprendre la synchronisation après un rechargement sans
  redemander le PIN. Il n'est jamais écrit dans le stockage partagé (`localStorage`) ni dans un cookie, et jamais journalisé. Le **PIN n'est
  jamais conservé**.
- Une fois le jeton expiré, la caisse **reste utilisable pour vendre** sur la session déjà ouverte (la vente est journalisée), mais la synchronisation
  attend une nouvelle connexion en ligne. Aucune connexion nouvelle n'est possible hors ligne.
- Le **code du commerce** saisi au premier lancement remplace l'enrôlement d'appareil, qui reste à construire côté serveur.
- Les identifiants de caisse et de point de vente sont générés sur l'appareil (UUID) en attendant leur gestion côté serveur.
- Aucune ressource d'API n'est mise en cache par le service worker (`ngsw-config.json` ne contient que les ressources de l'application).

## Conséquences

- Risque accepté : sur un appareil partagé, un jeton lisible tant qu'il n'a pas expiré. Mitigation : durée de 8 heures, déconnexion explicite
  qui l'efface, et effacement local à cadrer avant usage réel (`plan.md`).
- La vente hors ligne n'exige pas de revalider le PIN : à confirmer (question ouverte).

## Réexamen

À la validation du modèle d'authentification hors ligne, ou avant le premier déploiement sur un appareil partagé.
