# 0014 — Gestion complète de l'équipe : PIN, réactivation, rôle, invitations

**Statut :** proposé le 25/10/2026, à valider avec le porteur du projet. Complète `docs/specs/equipe` et `docs/decisions/0003` (identité par PIN), `0006` (appareils).

## Contexte

L'équipe sait se créer et se désactiver, mais quatre gestes courants manquent : un caissier **oublie son PIN**, un ancien caissier **revient**, un caissier devient **gérant**, un **lien d'ajout d'appareil** a été envoyé à la mauvaise personne. Aujourd'hui, il faut contourner (créer un second compte sous un autre nom, ou attendre 24 h l'expiration d'un lien).

Autre défaut trouvé en chemin : un jeton déjà émis **reste valable jusqu'à 8 h** après qu'on a désactivé un compte, et le rôle qu'il porte ne suit pas un changement de rôle. Pour un caissier qui part, 8 h de caisse ouverte est trop long.

## Décisions

1. **Le responsable choisit le nouveau PIN** (4 à 6 chiffres) et le communique de vive voix. Pas de PIN envoyé par SMS ou e-mail (aucun canal sûr, décision du projet). Le PIN n'est jamais écrit au journal ni renvoyé par le serveur ; la réinitialisation lève aussi un éventuel verrouillage.
2. **Mêmes droits que la création** (RolePolicy.CanManageRole) : le propriétaire gère tous les profils sauf un autre propriétaire ; le gérant gère les caissiers et les magasiniers. Personne ne change son propre rôle ni son propre PIN par ce geste, et le propriétaire n'est jamais touché (il a son mot de passe et sa réinitialisation par e-mail).
3. **Réactiver exige un nouveau PIN** : la personne a pu partir avec l'ancien dans la tête, ou le partager. On ne ressuscite pas un accès sans le renouveler.
4. **Changer de rôle est immédiat et se voit** : pour un rôle plus élevé, seul le propriétaire (un gérant ne fait pas de gérants) ; le journal garde « Awa Koné : Caissier → Gérant ». La personne doit se reconnecter.
5. **Une session s'arrête dès que le compte change** : un horodatage « sessions valables à partir de » est tenu par utilisateur ; il avance quand le PIN est réinitialisé, le compte désactivé ou réactivé, ou le rôle changé. L'API refuse (401, « Votre accès a changé : reconnectez-vous ») tout jeton émis avant. L'état est gardé 15 secondes en mémoire : on ne relit pas la base à chaque requête. Cela règle aussi le défaut des 8 heures après une désactivation.
6. **Un lien d'appareil se révoque tant qu'il n'est pas utilisé** : il disparaît des invitations en attente, et le lien ou le QR déjà envoyé répond comme un lien expiré (aucune information en plus sur ce qu'il a été). Un appareil déjà ajouté se retire comme avant.
7. **Les invitations en attente se voient** dans l'écran Appareils (nom proposé, qui l'a créée, quand elle expire) : on ne peut pas révoquer ce qu'on ne voit pas.
8. **Hors lot** : changer son propre PIN, PIN provisoire à changer à la première connexion, suppression d'un membre, transfert de propriété, changement du mot de passe du propriétaire connecté.

## Risques et limites

- Le responsable connaît le PIN qu'il a choisi : c'est le prix de l'absence de canal sûr ; la personne pourra le changer quand « changer mon PIN » existera (hors lot).
- 15 secondes de décalage au pire entre un changement et le refus d'un ancien jeton, sur chaque serveur.
- Un caissier hors ligne garde sa session locale jusqu'à sa prochaine connexion au serveur ; ses ventes se synchronisent normalement, les droits se contrôlent à la synchronisation.
