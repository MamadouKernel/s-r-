# Spécification — Gestion complète de l'équipe

**Statut :** brouillon à valider ; décisions de `docs/decisions/0014-gestion-complete-de-l-equipe.md`. Complète `docs/specs/equipe/spec.md`.
**Lots :** (1) serveur : PIN, réactivation, rôle, révocation d'invitation, fin immédiate des sessions ; (2) écrans : Équipe et Appareils.

## Objectif

Le propriétaire et le gérant font eux-mêmes ce que la vie d'une boutique impose : dépanner un caissier qui a oublié son PIN, reprendre un ancien caissier, promouvoir quelqu'un, annuler un lien d'appareil envoyé par erreur. Et un accès retiré ou modifié s'arrête **tout de suite**.

## Exigences

### EQP-01 Réinitialiser le PIN d'un membre
`POST /api/v1/users/{id}/pin` avec le nouveau PIN. Le profil cible est un membre **actif** à PIN (jamais le propriétaire). Droits : `RolePolicy.CanManageRole(acteur, cible)` ; jamais sur soi-même (403). Le PIN respecte la règle de création (4 à 6 chiffres, sinon 422 avec le message habituel). Le nouveau PIN remplace l'ancien, **les échecs de connexion repartent à zéro et un verrouillage éventuel est levé**. Le PIN n'est écrit nulle part (ni journal, ni réponse, ni journal technique). Journal : `user.pin.reset` (qui, pour qui). Toutes les sessions ouvertes de ce membre s'arrêtent (EQP-05).

### EQP-02 Réactiver un membre
`POST /api/v1/users/{id}/enable` avec un **nouveau PIN**. Le profil cible est un membre **désactivé**, mêmes droits que EQP-01. Le compte redevient actif avec le nouveau PIN, sans échec ni verrouillage ; son identifiant, son nom et son rôle sont conservés (et son historique : ventes, journal). Réactiver un compte déjà actif est refusé avec un message clair. Journal : `user.enabled`.

### EQP-03 Changer le rôle d'un membre
`PUT /api/v1/users/{id}/role` avec le nouveau rôle. Il faut pouvoir gérer **le rôle actuel et le nouveau** : le propriétaire peut passer un membre à gérant, caissier, magasinier ou comptable (jamais à propriétaire) ; un gérant ne change que caissier ↔ magasinier. Refusés : changer son propre rôle, toucher au propriétaire, changer le rôle d'un membre désactivé (réactivez-le d'abord), demander le rôle qu'il a déjà (message clair). Journal : `user.role.changed` avec « Awa Koné : Caissier → Gérant ». Les sessions du membre s'arrêtent (il se reconnecte avec son nouveau rôle).

### EQP-04 Invitations d'appareil
- **Voir** : `GET /api/v1/devices/invitations` liste les invitations **en attente** (ni utilisées, ni expirées, ni annulées) : nom proposé (ou « Appareil sans nom »), créée par (nom), créée le, expire le. Propriétaire et gérant. Jamais le secret ni le condensat.
- **Annuler** : `POST /api/v1/devices/invitations/{id}/revoke`. Possible tant que l'invitation n'est pas utilisée. Le lien ou le QR déjà envoyé répond ensuite exactement comme un lien expiré (même message, même code : rien n'apprend à celui qui le détient ce qu'il en est). Annuler deux fois ne change rien. Une invitation déjà utilisée ne s'annule pas (« Cet appareil est déjà ajouté : retirez-le dans la liste des appareils »). Journal : `device.invitation.revoked`.

### EQP-05 Une session s'arrête dès que le compte change
Chaque utilisateur porte une date « sessions valables à partir de ». Elle avance quand son PIN est réinitialisé, quand son compte est **désactivé** ou **réactivé**, et quand son **rôle** change. Un jeton émis **avant** cette date est refusé (401, code `session_outdated`, « Votre accès a changé : reconnectez-vous »). L'état est gardé 15 secondes en mémoire au plus. La connexion qui suit émet un jeton valable. Le propriétaire (mot de passe) n'est pas concerné par ces gestes.

### EQP-06 Droits et isolation
Caissier, magasinier et comptable : 403 partout. Un identifiant d'un autre commerce est introuvable (404). Un gérant ne peut ni réinitialiser, ni réactiver, ni promouvoir un gérant ou le propriétaire. Aucune de ces opérations n'est possible sur soi-même.

### EQP-07 Écrans (lot 2)
- **Équipe** : sur chaque membre, selon ses droits : « Nouveau PIN » (saisie du PIN et confirmation, rappel de le communiquer de vive voix), « Changer le rôle » (liste des rôles permis, avertissement « la personne devra se reconnecter »), « Désactiver » (déjà là) ou « Réactiver » (avec un nouveau PIN). Les membres désactivés sont visibles (case « Afficher les membres désactivés »). Chaque geste confirme en mots (« Le PIN d'Awa Koné a été changé. Dites-le-lui de vive voix. ») ; les erreurs sont en français.
- **Appareils** : une section « Liens en attente » sous le bouton « Ajouter un appareil » : nom, créé par, expire (« dans 5 h »), bouton « Annuler ce lien » avec confirmation.
- **Session arrêtée** : quand le serveur répond `session_outdated`, l'application dit « Votre accès a changé : reconnectez-vous » et renvoie à la connexion, sans perdre les ventes en attente d'envoi.

## Scénarios d'acceptation

1. **PIN oublié.** Le gérant donne le PIN 4821 à une caissière verrouillée après 5 échecs : elle se connecte aussitôt avec 4821 ; l'ancien PIN est refusé ; le journal dit « PIN réinitialisé » sans le PIN.
2. **PIN invalide.** « 12 », « abcd », « 1234567 » : 422 ; rien ne change.
3. **Droits.** Un gérant ne réinitialise ni un gérant, ni le propriétaire, ni lui-même ; un caissier n'a aucun de ces gestes ; le propriétaire ne peut pas être visé.
4. **Retour d'un caissier.** Un caissier désactivé est réactivé avec un nouveau PIN : il se connecte avec le nouveau PIN, retrouve son historique, l'ancien PIN est refusé.
5. **Réactiver un actif** : message clair. **Réactiver sans PIN valide** : 422.
6. **Promotion.** Le propriétaire passe une caissière à gérante : à sa reconnexion elle a le rôle gérant ; le journal dit « Caissier → Gérant ».
7. **Limites.** Un gérant ne peut pas promouvoir à gérant ; personne ne se change soi-même ; le propriétaire ne devient pas « propriétaire » deux fois ; même rôle demandé : message clair ; membre désactivé : refusé.
8. **Fin immédiate.** Un caissier connecté est désactivé : sa requête suivante (au plus 15 s plus tard) reçoit 401 `session_outdated`. Même effet après un changement de rôle ou de PIN. Après sa nouvelle connexion, tout refonctionne.
9. **Lien envoyé par erreur.** Le gérant voit le lien en attente, l'annule ; le QR déjà envoyé répond comme un lien expiré ; la liste ne le montre plus ; l'annuler de nouveau ne change rien.
10. **Lien déjà utilisé.** L'annulation est refusée avec le message qui renvoie vers la liste des appareils.
11. **Isolation.** Le commerce B ne voit pas les invitations du commerce A et ne peut pas les annuler (404).
12. **Pas de fuite.** Aucune réponse, aucun journal, aucun fichier n'écrit un PIN ni un secret d'invitation.

## Hors lot

- Changer son propre PIN ; PIN provisoire à changer à la première connexion ; envoi d'un PIN par SMS ou e-mail.
- Suppression d'un membre ; transfert de la propriété ; second propriétaire.
- Changement du mot de passe du propriétaire quand il est connecté (reste un manque à traiter à part).
