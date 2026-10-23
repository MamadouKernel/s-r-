# Spécification : inscription d'un commerce, premier propriétaire et appareils

**Statut :** brouillon ; décisions prises avec le porteur du projet le 23/10/2026 (voir `docs/decisions/0005-inscription-proprietaire-et-sms.md`).
Le chantier est livré en **trois PR** : (1) serveur, inscription ; (2) serveur, appareils ; (3) écrans. Ce document couvre les trois ; chaque exigence indique sa PR.

## Objectif

Permettre à un commerçant de créer son commerce lui-même, depuis son téléphone, de devenir son propriétaire, puis de rattacher ses caisses de façon contrôlée
et révocable. Aujourd'hui, aucun commerce réel ne peut être créé et un appareil se rattache en saisissant à la main un identifiant de 36 caractères.

## Décisions (rappel)

- **Le propriétaire se connecte par téléphone et mot de passe, sans code SMS** (dérogation au cahier, consignée dans l'ADR 0005). Le SMS sert à **vérifier le téléphone à l'inscription** et à **réinitialiser un mot de passe perdu**.
- **Passerelle SMS** : un contrat (`ISmsSender`) et un adaptateur de développement. Le serveur refuse de démarrer hors développement sans fournisseur réel.
- **Essai gratuit de 30 jours**, formule « Commerce », sans paiement ; seule la date de fin est enregistrée (lecture seule RG-14 : avec l'abonnement).
- **Appareils : enrôlement par lien ou QR à usage unique, appareils révocables** (PR 2 et 3).
- **Un seul compte propriétaire par numéro de téléphone**, numéro ivoirien (+225, 10 chiffres) pour l'instant.
- Rien n'est créé avant la vérification du SMS.

## Exigences

### INS-01 Demande d'inscription (PR 1)
`POST /api/v1/signup/start` (sans jeton) : numéro de téléphone, nom du commerce (2 à 100 caractères), type d'activité (supérette, boutique, quincaillerie, pharmacie, restaurant ou maquis,
salon, atelier, autre), nom du propriétaire, version des conditions acceptée. Le numéro est normalisé en `+225XXXXXXXXXX` (formats acceptés : `07 07 07 07 07`, `+225 07 07 07 07 07`, `00225…`).
Réponse : identifiant de vérification, heure d'expiration, heure à partir de laquelle un renvoi est possible. **Aucune ligne de commerce ni d'utilisateur n'est créée.**
Refus : numéro invalide (422), conditions non acceptées ou en mauvaise version (422), numéro déjà inscrit (409 `phone_already_registered`), trop de demandes (429).

### INS-02 Code de vérification (PR 1)
Code à 6 chiffres tiré aléatoirement, valable 10 minutes, 5 essais, jamais stocké en clair (condensat avec un secret du serveur), jamais renvoyé par l'API.
Renvoi : `POST /api/v1/signup/resend`, au plus 3 fois et pas avant 60 secondes ; chaque renvoi remplace le code précédent.
Au plus 3 demandes d'inscription par numéro et par jour.

### INS-03 Finalisation (PR 1)
`POST /api/v1/signup/complete` : identifiant de vérification, code, mot de passe. Si le code est exact et le mot de passe accepté, une **seule transaction** crée le commerce (avec sa date de fin d'essai à +30 jours,
la version des conditions et la date d'acceptation), le propriétaire et son entrée d'annuaire, trace `commerce.created` dans le journal, puis connecte le propriétaire (jeton). Un mot de passe refusé **ne consomme pas** le code.
Code faux : 422 `invalid_code` avec le nombre d'essais restants ; code expiré ou essais épuisés : 422 `verification_expired`.

### INS-04 Mot de passe du propriétaire (PR 1)
Au moins 10 caractères (128 au plus), au moins une lettre et un chiffre, différent du numéro de téléphone. Condensat lent et salé avec le secret du serveur, comme le PIN. Jamais journalisé ni renvoyé.

### INS-05 Connexion du propriétaire (PR 1)
`POST /api/v1/auth/owner/login` : téléphone et mot de passe → jeton, commerce, profil. Même réponse (401 `invalid_credentials`) pour un numéro inconnu, un compte désactivé ou un mauvais mot de passe, avec le même temps de calcul ;
seul un compte verrouillé reçoit une réponse distincte (423). Verrouillage : 5 échecs consécutifs, 15 minutes. Limitation de débit stricte par adresse.

### INS-06 Mot de passe oublié (PR 1)
`POST /api/v1/owner/password-reset/start` répond toujours 202, que le numéro existe ou non (aucune fuite sur les comptes) ; si le propriétaire existe, un code est envoyé (3 demandes par jour au plus).
`POST /api/v1/owner/password-reset/confirm` : numéro, code, nouveau mot de passe → 204. Le code a les mêmes règles que l'inscription ; un succès lève le verrouillage et est tracé (`user.password.reset`).

### INS-07 Protection contre les abus (PR 1)
Limitation de débit par adresse sur toutes les routes publiques d'inscription et de réinitialisation ; plafond par numéro et par jour ; aucun SMS n'est envoyé pour un numéro invalide ou déjà inscrit.

### INS-08 Invitation d'enrôlement (PR 2)
Un propriétaire ou un gérant crée une invitation d'appareil : jeton aléatoire à usage unique, valable 24 heures, stocké sous forme de condensat. Réponse : lien et contenu du QR.

### INS-09 Enrôlement et appareils révocables (PR 2)
L'appareil présente le jeton : il devient un **appareil** du commerce (nom, date, dernière activité) et reçoit un secret propre, conservé sur l'appareil. La connexion d'un caissier exige un appareil valide ;
la **révocation** d'un appareil refuse immédiatement ses connexions et ses requêtes en cours de session. Liste des appareils, révocation par le propriétaire ou le gérant, traces dans le journal.

### INS-10 Écrans (PR 3)
Inscription en trois étapes (commerce, vérification du code, mot de passe), connexion du propriétaire, mot de passe oublié, invitation d'appareil (lien et QR), configuration d'une caisse par lien ou QR,
liste des appareils avec révocation. États : chargement, erreur, hors ligne ; 320 px sans débordement.

## Scénarios d'acceptation (PR 1)

- **SC-I1** Un commerçant s'inscrit : demande, code reçu par SMS, finalisation ; il est connecté, son commerce existe avec 30 jours d'essai et il peut ensuite se connecter par téléphone et mot de passe.
- **SC-I2** Aucune ligne de commerce ni d'utilisateur n'existe tant que le code n'est pas validé.
- **SC-I3** Un code faux décompte les essais ; au cinquième, la demande est bloquée même avec le bon code ; un code expiré est refusé.
- **SC-I4** Un mot de passe faible est refusé sans consommer le code, qui reste utilisable.
- **SC-I5** Un numéro déjà inscrit est refusé (409) sans envoyer de SMS ; un numéro invalide aussi (422).
- **SC-I6** Le quatrième essai d'inscription d'une journée pour un même numéro est refusé (429).
- **SC-I7** Le renvoi est refusé avant 60 secondes et après 3 renvois ; le nouveau code remplace l'ancien.
- **SC-I8** Numéro inconnu, compte désactivé et mauvais mot de passe donnent la même réponse ; cinq échecs verrouillent le compte (423).
- **SC-I9** « Mot de passe oublié » répond pareil pour un numéro inconnu ; avec le bon code, le nouveau mot de passe fonctionne, l'ancien non, et le verrouillage est levé.
- **SC-I10** Un autre commerce ne voit jamais le commerce, ses utilisateurs ni ses données ; le jeton du nouveau propriétaire est limité à son commerce.
- **SC-I11** Hors développement, le serveur refuse de démarrer sans fournisseur SMS réel.

## Hors lot

Import Excel des produits, premier point de vente et sa numérotation, choix de la formule et paiement de l'abonnement, plusieurs commerces pour un même propriétaire, numéros étrangers,
code SMS à la connexion du propriétaire (voir l'ADR 0005 : réexamen), changement de numéro de téléphone.
