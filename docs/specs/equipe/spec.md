# Spécification : équipe (lecture des utilisateurs côté serveur et écran Équipe)

**Statut :** brouillon ; s'appuie sur `docs/decisions/0003-identite-pin-et-jetons.md`. Les maquettes ne sont pas validées avec le pilote (T030).

## Objectif

Permettre au propriétaire et au gérant de voir leur équipe, d'ajouter un caissier ou un magasinier et de désactiver un départ, et afficher le **nom** des auteurs
dans l'historique du stock (aujourd'hui : un identifiant).

## Décisions

- Le serveur reste l'arbitre des droits : il calcule lui-même, pour chaque utilisateur, s'il peut être désactivé par le profil connecté (`CanDisable`),
  et la liste des profils que celui-ci peut créer (`AssignableRoles`). L'écran n'en recopie pas la règle.
- **Jamais de condensat de PIN** dans une réponse : les vues sont construites à part, pas à partir de l'entité.
- Deux lectures :
  - `GET /api/v1/users` : l'équipe complète, pour le propriétaire et le gérant ;
  - `GET /api/v1/users/directory` : identifiant et nom seulement, pour les profils qui lisent l'historique du stock (propriétaire, gérant, magasinier).
- Hors lot, faute de décision ou de serveur : réinitialiser un PIN, réactiver un compte, changer le rôle, supprimer. Un compte se désactive, jamais ne se supprime.

## Exigences

### EQ-01 Lecture de l'équipe (serveur)
`GET /api/v1/users` renvoie `{ users, assignableRoles }`. Chaque utilisateur : identifiant, nom, rôle, statut (actif ou désactivé), date de création, dernière connexion,
verrouillé jusqu'à (si verrouillé), `canDisable`, `isSelf`. Tri : actifs d'abord, puis par nom. Propriétaire et gérant seulement ; les autres profils reçoivent 403.
Le commerce vient du jeton : un commerce ne voit jamais les utilisateurs d'un autre (contrôle applicatif et sécurité par ligne).

### EQ-02 Annuaire (serveur)
`GET /api/v1/users/directory` renvoie `[{ id, displayName }]` des utilisateurs du commerce (actifs ou non, car un ancien employé reste l'auteur de mouvements passés).
Propriétaire, gérant et magasinier ; caissier et comptable : 403.

### EQ-03 Écran Équipe
Dans l'espace Gestion, réservé au propriétaire et au gérant : liste de l'équipe (nom, identifiant, rôle en français, état en toutes lettres : actif, désactivé, verrouillé jusqu'à…, dernière connexion),
bouton « Ajouter un membre », bouton « Désactiver » seulement quand `canDisable`, avec confirmation explicite. États : chargement, vide, erreur, hors ligne.

### EQ-04 Ajout d'un membre
Identifiant (3 à 32 caractères : lettres, chiffres, point, tiret, souligné), nom, rôle parmi `assignableRoles`, code PIN de 4 à 6 chiffres saisi deux fois.
L'identifiant est unique dans le commerce (409 signalé sous le champ). L'identifiant d'utilisateur est fixé à l'ouverture du formulaire : un nouvel essai ne crée jamais deux comptes.
Le PIN n'est ni affiché, ni conservé, ni journalisé ; le champ est vidé après chaque envoi.

### EQ-05 Désactivation
Confirmation explicite (« Désactiver Awa Koné ? Elle ne pourra plus se connecter. Ses ventes passées restent attribuées à son nom. ») ; un jeton déjà émis reste valide jusqu'à son expiration (8 h), ce que le message rappelle.

### EQ-06 Auteurs dans l'historique du stock
L'historique d'un produit affiche le nom de l'auteur (annuaire) ; si l'annuaire n'est pas disponible, l'identifiant court reste affiché.

## Scénarios d'acceptation

- **SC-E1** Un gérant voit son équipe, peut désactiver un caissier mais pas un autre gérant ni le propriétaire.
- **SC-E2** Un propriétaire peut ajouter un gérant ; un gérant ne propose que « Caissier » et « Magasinier ».
- **SC-E3** Un caissier ou un comptable reçoit 403 sur `GET /users` et n'a pas l'écran Équipe.
- **SC-E4** Un identifiant déjà pris est signalé sous le champ et ne crée rien.
- **SC-E5** Aucune réponse du serveur ne contient de condensat de PIN.
- **SC-E6** Un utilisateur d'un autre commerce n'apparaît ni dans la liste ni dans l'annuaire.
- **SC-E7** L'historique du stock affiche « Moussa Traoré » au lieu d'un identifiant.
- **SC-E8** Un double appui sur « Ajouter » ne crée qu'un compte.

## Questions ouvertes

- Réinitialisation du PIN par le gérant, réactivation d'un compte, changement de rôle : à décider avec le pilote (qui valide quoi, trace d'audit).
- Un utilisateur peut-il se désactiver lui-même ? Aujourd'hui non (`CanManageRole` ne le permet pas pour son propre rôle sauf cas particulier) ; `canDisable` est faux pour soi-même.
