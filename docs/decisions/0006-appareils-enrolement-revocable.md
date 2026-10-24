# 0006 — Appareils : enrôlement par lien ou QR, révocation immédiate

**Statut :** accepté le 24/10/2026 (décision du porteur du projet : « lien ou QR d'enrôlement révocable »).

## Contexte

Une caisse se rattachait en saisissant un « code du commerce » de 36 caractères (ADR 0004), inutilisable pour un commerçant, et rien n'empêchait un téléphone perdu ou un ancien employé de continuer à se connecter.

## Décision

1. **Invitation à usage unique** : le propriétaire ou le gérant crée une invitation valable 24 heures. Le jeton (commerce + invitation + secret aléatoire de 256 bits) devient un lien et un QR ; seul un condensat HMAC du secret est stocké.
2. **Enrôlement** : l'appareil présente le jeton, consomme l'invitation et reçoit un **secret d'appareil** (256 bits, montré une fois, condensat HMAC en base). Il le garde dans son stockage local ; le commerçant ne voit jamais ni identifiant ni code.
3. **Connexion par PIN liée à l'appareil** : identifiant + secret de l'appareil vérifiés avant le compte (un appareil refusé ne peut pas verrouiller un compte). Le jeton d'accès porte l'appareil.
4. **Révocation immédiate** : un contrôle par requête (une lecture indexée, sous isolation par commerce) refuse tout jeton d'un appareil révoqué. Prix : une lecture par requête ; la « dernière activité » n'est réécrite qu'au plus toutes les 5 minutes.
5. **Réglage `Devices:Required`** (vrai par défaut) : la connexion sans appareil n'est tolérée que le temps que les écrans d'enrôlement arrivent (PR 3), uniquement en développement.
6. **Propriétaire sans appareil** : sa connexion par mot de passe (ADR 0005) n'est pas liée à un appareil.

## Risques et limites

- Un lien envoyé à la mauvaise personne, valable 24 h, enrôle un appareil inconnu : le propriétaire le voit dans la liste et le révoque. Une révocation d'invitation avant usage n'est pas prévue (hors lot).
- Les ventes non envoyées d'un appareil révoqué ne partent plus.
- Le secret d'appareil est conservé sur l'appareil (stockage du navigateur) : il protège contre un compte de caissier utilisé depuis un autre téléphone, pas contre quelqu'un qui possède l'appareil déverrouillé.

## Réexamen

À la validation avec le pilote, ou si un incident d'appareil survient.
