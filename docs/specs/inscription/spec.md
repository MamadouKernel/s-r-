# Spécification : inscription d'un commerce, premier propriétaire et appareils

**Statut :** brouillon ; décisions prises avec le porteur du projet le 23/10/2026 (voir `docs/decisions/0005-inscription-proprietaire-et-email.md`).
Le chantier est livré en **trois PR** : (1) serveur, inscription ; (2) serveur, appareils ; (3) écrans. Ce document couvre les trois ; chaque exigence indique sa PR.

## Objectif

Permettre à un commerçant de créer son commerce lui-même, depuis son téléphone ou son ordinateur, de devenir son propriétaire, puis de rattacher ses caisses de façon contrôlée
et révocable. Aujourd'hui, aucun commerce réel ne peut être créé et un appareil se rattache en saisissant à la main un identifiant de 36 caractères.

## Décisions (rappel)

- **Le propriétaire se connecte par adresse e-mail et mot de passe, sans code à chaque connexion** (dérogation au cahier, consignée dans l'ADR 0005). L'e-mail sert à **vérifier l'adresse à l'inscription** et à **réinitialiser un mot de passe perdu**.
- **Envoi d'e-mails** : un contrat (`IEmailSender`) et un adaptateur de développement. Le serveur refuse de démarrer hors développement sans serveur d'envoi (SMTP, MailKit).
- **Essai gratuit de 30 jours**, formule « Commerce », sans paiement ; seule la date de fin est enregistrée (lecture seule RG-14 : avec l'abonnement).
- **Appareils : enrôlement par lien ou QR à usage unique, appareils révocables** (PR 2 et 3).
- **Un seul compte propriétaire par adresse e-mail**, toute adresse valide (le code reçu prouve qu'elle existe et appartient au commerçant). Ni SMS ni WhatsApp : ni coût ni fournisseur à valider (ADR 0005, évolution du 23/10/2026).
- Rien n'est créé avant la vérification de l'e-mail.

## Exigences

### INS-01 Demande d'inscription (PR 1)
`POST /api/v1/signup/start` (sans jeton) : adresse e-mail, nom du commerce (2 à 100 caractères), type d'activité (supérette, boutique, quincaillerie, pharmacie, restaurant ou maquis,
salon, atelier, autre), nom du propriétaire, version des conditions acceptée. L'adresse est normalisée (espaces retirés, minuscules) : `Mamadou@Exemple.CI` et `mamadou@exemple.ci` sont le même compte.
Réponse : identifiant de vérification, heure d'expiration, heure à partir de laquelle un renvoi est possible. **Aucune ligne de commerce ni d'utilisateur n'est créée.**
Refus : adresse invalide (422), conditions non acceptées ou en mauvaise version (422), adresse déjà inscrite (409 `email_already_registered`), trop de demandes (429).

### INS-02 Code de vérification (PR 1)
Code à 6 chiffres tiré aléatoirement, valable 10 minutes, 5 essais, jamais stocké en clair (condensat avec un secret du serveur), jamais renvoyé par l'API.
Renvoi : `POST /api/v1/signup/resend`, au plus 3 fois et pas avant 60 secondes ; chaque renvoi remplace le code précédent.
Au plus 3 demandes d'inscription par adresse et par jour.

### INS-03 Finalisation (PR 1)
`POST /api/v1/signup/complete` : identifiant de vérification, code, mot de passe. Si le code est exact et le mot de passe accepté, une **seule transaction** crée le commerce (avec sa date de fin d'essai à +30 jours,
la version des conditions et la date d'acceptation), le propriétaire et son entrée d'annuaire, trace `commerce.created` dans le journal, puis connecte le propriétaire (jeton). Un mot de passe refusé **ne consomme pas** le code.
Code faux : 422 `invalid_code` avec le nombre d'essais restants ; code expiré ou essais épuisés : 422 `verification_expired`.

### INS-04 Mot de passe du propriétaire (PR 1)
Au moins 10 caractères (128 au plus), au moins une lettre et un chiffre, différent du adresse e-mail. Condensat lent et salé avec le secret du serveur, comme le PIN. Jamais journalisé ni renvoyé.

### INS-05 Connexion du propriétaire (PR 1)
`POST /api/v1/auth/owner/login` : adresse e-mail et mot de passe → jeton, commerce, profil. Même réponse (401 `invalid_credentials`) pour une adresse inconnue, un compte désactivé ou un mauvais mot de passe, avec le même temps de calcul ;
seul un compte verrouillé reçoit une réponse distincte (423). Verrouillage : 5 échecs consécutifs, 15 minutes. Limitation de débit stricte par adresse.

### INS-06 Mot de passe oublié (PR 1)
`POST /api/v1/owner/password-reset/start` répond toujours 202, que l'adresse existe ou non (aucune fuite sur les comptes) ; si le propriétaire existe, un code est envoyé (3 demandes par jour au plus).
`POST /api/v1/owner/password-reset/confirm` : adresse, code, nouveau mot de passe → 204. Le code a les mêmes règles que l'inscription ; un succès lève le verrouillage et est tracé (`user.password.reset`).

### INS-07 Protection contre les abus (PR 1)
Limitation de débit par adresse sur toutes les routes publiques d'inscription et de réinitialisation ; plafond par adresse et par jour ; aucun e-mail n'est envoyé pour une adresse invalide ou déjà inscrit.

### INS-08 Invitation d'appareil (PR 2)
`POST /api/v1/devices/invitations` (propriétaire ou gérant) : crée une invitation à usage unique, valable **24 heures**, avec un nom d'appareil suggéré facultatif (« Caisse 1 », 2 à 50 caractères).
Réponse : un **jeton** (long, aléatoire) et son heure d'expiration. Le jeton ne contient aucun secret lisible, n'est stocké que sous forme de condensat, et n'apparaît jamais dans les journaux. Le client en fait un **lien** et un **QR** (le jeton va dans la partie `#` du lien, qui n'est jamais envoyée à un serveur).
Tracé : `device.invited`. Un caissier ne peut pas créer d'invitation (403).

### INS-09 Enrôlement, appareils et révocation (PR 2)
- `POST /api/v1/devices/enroll` (sans jeton d'accès) : jeton d'invitation et nom d'appareil facultatif. Si l'invitation est valide, elle est **consommée** et l'appareil est créé ; la réponse donne l'identifiant de l'appareil, **son secret (montré une seule fois)**, le commerce et son nom.
  Le secret n'est stocké que sous forme de condensat. Refus : lien invalide, **déjà utilisé** ou **expiré** (422, messages distincts et clairs, sans jargon). Deux appareils qui présentent le même lien en même temps : un seul gagne.
  Plafond de débit par adresse. Tracé : `device.enrolled`.
- **Connexion par PIN** : la requête porte l'appareil (identifiant et secret). Appareil inconnu, secret faux ou appareil révoqué : 403 `device_not_authorized` (« Cet appareil n'est plus autorisé… »), **avant** de toucher au compteur d'échecs du compte.
  Le jeton d'accès porte l'appareil. Réglage `Devices:Required` (vrai par défaut) ; faux seulement le temps que les écrans arrivent (PR 3), jamais en production.
- **Révocation** : `POST /api/v1/devices/{id}/revoke` (propriétaire ou gérant, idempotent, tracé `device.revoked`). Dès la révocation, **chaque requête** portant un jeton de cet appareil est refusée (401 `device_not_authorized`), même si le jeton n'a pas expiré, et aucune connexion n'est plus possible.
- `GET /api/v1/devices` : nom, date d'enrôlement, dernière activité (mise à jour au plus toutes les 5 minutes), état (actif ou révoqué).
- Le propriétaire (connexion par mot de passe) n'a pas besoin d'appareil.
- Conséquence assumée : les ventes encore non envoyées d'un appareil révoqué ne partent plus (à signaler dans l'écran de révocation, PR 3).

### INS-10 Écrans (PR 3)
Principe : **personne ne saisit ni ne voit jamais un identifiant technique**. L'utilisateur tape son adresse e-mail, un code de 6 chiffres reçu par e-mail, un nom, un mot de passe ou un PIN ; tout le reste (identifiants de demande, de commerce, d'appareil, secrets) reste dans l'application.
Chaque écran : chargement, erreur (en français simple, avec ce qu'il faut faire), hors ligne, 320 px sans défilement horizontal, cibles de 48 px, état jamais porté par la seule couleur.

- **Bienvenue** (`/bienvenue`, appareil non configuré) : trois choix en grands boutons : « Créer ma boutique », « J'ai déjà une boutique » (propriétaire), « J'ai reçu un lien » (explique : ouvrez le lien ou scannez le QR reçu du propriétaire). Si l'appareil a été retiré, un message le dit et dit quoi faire.
- **Inscription** (`/inscription`), deux écrans : (1) la boutique : nom, type d'activité en mots (« Supérette », « Boutique », « Quincaillerie »…), nom du propriétaire, adresse e-mail, case « J'accepte les conditions d'utilisation » ; (2) la vérification : « Nous avons envoyé un code à… », code, mot de passe (règle écrite avant la saisie, affichage possible), « Renvoyer le code » (compte à rebours de 60 s puis actif), « Changer d'adresse e-mail ». Un code faux ramène au champ du code avec le nombre d'essais restants. Au succès, le propriétaire est connecté et arrive sur son tableau de bord (30 jours d'essai).
- **Connexion du propriétaire** (`/connexion-proprietaire`) : adresse e-mail, mot de passe (affichage possible), « Mot de passe oublié ? », « Créer ma boutique ».
- **Mot de passe oublié** (`/mot-de-passe-oublie`) : adresse e-mail ; réponse toujours la même (« Si cette adresse a un compte, un code vient d'être envoyé ») ; puis code et nouveau mot de passe ; au succès retour à la connexion avec « Mot de passe changé ».
- **Ajouter un appareil** (`/gestion/appareils`, propriétaire et gérant) : liste des appareils (nom, état en toutes lettres, dernière activité) ; « Ajouter un appareil » : nom facultatif, puis **QR code et lien** à envoyer (« Valable 24 heures, une seule fois »), boutons « Copier le lien » et « Partager » (le partage système, par la messagerie que la personne choisit) ; « Retirer » avec confirmation qui dit ce que cela implique (appareil coupé, ventes non envoyées perdues).
- **Rejoindre** (`/rejoindre#…`) : le lien du propriétaire ; le jeton se lit dans la partie `#` de l'adresse (jamais envoyée à un serveur) ; l'écran demande seulement le nom de l'appareil (« Caisse » proposé) et un bouton « Configurer cet appareil » ; lien utilisé, expiré ou incomplet : message clair et quoi faire.
- **Connexion des caissiers** : inchangée (identifiant et PIN) ; l'appareil enrôlé est joint automatiquement ; un appareil retiré affiche « Cet appareil n'est plus autorisé » et ramène à Bienvenue en gardant les ventes déjà enregistrées sur l'appareil.
- Le « Code du commerce » de 36 caractères de l'ancien écran de configuration **disparaît**.
- Conditions d'utilisation : une page provisoire (`/conditions`) marquée comme à valider ; la version acceptée est celle que le serveur attend.

## Scénarios d'acceptation (PR 1)

- **SC-I1** Un commerçant s'inscrit : demande, code reçu par e-mail, finalisation ; il est connecté, son commerce existe avec 30 jours d'essai et il peut ensuite se connecter par adresse e-mail et mot de passe.
- **SC-I2** Aucune ligne de commerce ni d'utilisateur n'existe tant que le code n'est pas validé.
- **SC-I3** Un code faux décompte les essais ; au cinquième, la demande est bloquée même avec le bon code ; un code expiré est refusé.
- **SC-I4** Un mot de passe faible est refusé sans consommer le code, qui reste utilisable.
- **SC-I5** Une adresse déjà inscrite est refusée (409) sans envoyer d'e-mail ; une adresse invalide aussi (422).
- **SC-I6** Le quatrième essai d'inscription d'une journée pour une même adresse est refusé (429).
- **SC-I7** Le renvoi est refusé avant 60 secondes et après 3 renvois ; le nouveau code remplace l'ancien.
- **SC-I8** Adresse inconnue, compte désactivé et mauvais mot de passe donnent la même réponse ; cinq échecs verrouillent le compte (423).
- **SC-I9** « Mot de passe oublié » répond pareil pour une adresse inconnue ; avec le bon code, le nouveau mot de passe fonctionne, l'ancien non, et le verrouillage est levé.
- **SC-I10** Un autre commerce ne voit jamais le commerce, ses utilisateurs ni ses données ; le jeton du nouveau propriétaire est limité à son commerce.
- **SC-I11** Hors développement, le serveur refuse de démarrer sans serveur d'envoi réel.

## Scénarios d'acceptation des appareils (PR 2)

- **SC-A1** Le gérant crée une invitation ; un caissier ne le peut pas (403) ; le jeton n'est jamais journalisé et n'est stocké que sous forme de condensat.
- **SC-A2** Un appareil présente le jeton : il est enrôlé, reçoit son secret une seule fois, l'invitation est consommée ; le même jeton une seconde fois est refusé (« déjà utilisé »).
- **SC-A3** Une invitation de plus de 24 heures est refusée (« expiré ») ; un jeton falsifié ou tronqué est refusé sans détail.
- **SC-A4** Deux appareils présentant le même jeton en même temps : un seul est enrôlé.
- **SC-A5** La connexion par PIN exige un appareil valide ; sans appareil, avec un mauvais secret ou un appareil d'un autre commerce : refus 403, et le compteur d'échecs du compte ne bouge pas.
- **SC-A6** Après révocation, le jeton d'accès déjà émis ne passe plus (401) et la connexion est refusée ; les autres appareils continuent.
- **SC-A7** Un autre commerce ne voit ni les appareils ni les invitations de celui-ci et ne peut pas les révoquer.
- **SC-A8** Création, enrôlement et révocation sont dans le journal des actions sensibles (catégorie « appareils »).

## Scénarios d'acceptation des écrans (PR 3)

- **SC-U1** Un commerçant crée sa boutique de bout en bout depuis un téléphone de 320 px sans voir un seul identifiant ni terme technique, et arrive sur son tableau de bord.
- **SC-U2** Un code faux, un code expiré, une adresse déjà inscrite, un mot de passe trop faible, un serveur d'envoi en panne et une coupure réseau donnent chacun un message clair qui dit quoi faire.
- **SC-U3** Le gérant ajoute un appareil, obtient un QR et un lien, le caissier ouvre le lien, nomme l'appareil et se connecte avec son PIN ; l'appareil apparaît dans la liste.
- **SC-U4** Un lien déjà utilisé ou expiré est expliqué en mots simples ; un lien incomplet aussi.
- **SC-U5** Le gérant retire un appareil après confirmation ; sur cet appareil, la requête suivante le renvoie à Bienvenue avec un message, sans perdre les ventes enregistrées.
- **SC-U6** « Mot de passe oublié » : même message que l'adresse existe ou non ; avec le code, nouveau mot de passe, retour à la connexion.

## Hors lot

Import Excel des produits, premier point de vente et sa numérotation, choix de la formule et paiement de l'abonnement, plusieurs commerces pour un même propriétaire, adresses hors Côte d'Ivoire,
code par e-mail à la connexion du propriétaire (voir l'ADR 0005 : réexamen), changement de adresse e-mail.
