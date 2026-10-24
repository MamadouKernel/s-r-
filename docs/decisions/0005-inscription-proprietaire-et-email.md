# 0005 — Inscription, premier propriétaire et vérification par e-mail

**Statut :** accepté le 23/10/2026 (décisions du porteur du projet). Contient **une dérogation au cahier des charges**, motivée ci-dessous.

## Contexte

Aucun commerce réel ne peut être créé : la table `commerce` n'existe pas encore (les commerces ne sont que des identifiants dans les autres tables), aucun point d'entrée public ne crée un premier propriétaire
(ADR 0003), et une caisse se rattache en saisissant un identifiant de 36 caractères. Le cahier des charges prévoit « mot de passe et code par SMS » pour le propriétaire (section 12) et une inscription par téléphone avec essai gratuit
(section 11, durée à définir).

## Décisions

1. **Inscription en deux temps, sans rien créer avant la vérification.** Une demande (`email_verifications`) porte les informations saisies et un code par e-mail ; le commerce, le propriétaire et son entrée d'annuaire
   ne sont créés qu'à la finalisation, dans une seule transaction.
2. **Un propriétaire par adresse e-mail** (adresse normalisée en minuscules). Une table d'annuaire sans sécurité par ligne (`owner_directory` : adresse → commerce) permet à un propriétaire de se connecter sans connaître
   l'identifiant de son commerce ; elle ne contient aucun secret. Le commerce lui-même (`commerces`) reste protégé par la sécurité par ligne.
3. **Le propriétaire se connecte par adresse et mot de passe.** *Dérogation* : le cahier ajoute un code par e-mail ; il n'est pas demandé à la connexion (voir ci-dessous).
4. **Mot de passe** : 10 à 128 caractères, au moins une lettre et un chiffre, ne contenant ni l'adresse e-mail ni sa partie avant le @. Même condensat que le PIN (HMAC avec le secret du serveur puis PBKDF2 salé, 600 000 itérations).
5. **Code par e-mail** : 6 chiffres aléatoires, 10 minutes, 5 essais, 3 renvois espacés de 60 secondes, 3 demandes par adresse et par jour ; stocké sous forme de condensat lié à la demande, jamais en clair, jamais renvoyé par l'API.
6. **Envoi d'e-mails** : un contrat `IEmailSender` et un adaptateur de développement qui écrit le message dans les journaux. Le serveur d'envoi (SMTP, MailKit) se choisit avant le pilote. **Le serveur refuse de démarrer hors développement
   et hors tests tant qu'aucun serveur d'envoi (SMTP, MailKit) n'est configuré** (`Mail:Smtp:Host`), pour qu'aucun code ne soit « envoyé » dans le vide.
7. **Essai gratuit de 30 jours**, formule « Commerce » ; la date de fin est enregistrée, la mise en lecture seule (RG-14) viendra avec l'abonnement.
8. **Mot de passe oublié** : réponse identique pour un adresse connue ou inconnue ; un code par e-mail valide permet de définir un nouveau mot de passe, ce qui lève le verrouillage et est tracé.
9. **Conditions d'utilisation** : l'acceptation (version et date) est enregistrée avec le commerce. Le texte juridique est fourni par le porteur du projet ; la version courante est une configuration (`Signup:TermsVersion`).
10. **Appareils (décidé, livré en PR 2 et 3)** : enrôlement par lien ou QR à usage unique, appareils révocables, la révocation coupant l'accès immédiatement.

## Dérogation : pas de code par e-mail à la connexion du propriétaire

- **Motivation** : un e-mail à chaque connexion ralentit le propriétaire et dépend de la livraison du courrier ; le propriétaire se connecte souvent, depuis son téléphone ou son ordinateur.
- **Périmètre** : la connexion du propriétaire seulement. Le PIN des caissiers, l'e-mail d'inscription et l'e-mail de réinitialisation ne changent pas.
- **Risque** : un mot de passe volé ou deviné donne accès à toutes les ventes, au stock et à l'équipe du commerce. Il n'y a pas de second facteur.
- **Compensations** : mot de passe d'au moins 10 caractères, verrouillage (5 échecs, 15 minutes), limitation de débit par adresse, réponses indiscernables (pas de fuite sur les comptes), journal d'audit, réinitialisation par SMS.
  La connexion est écrite de façon à pouvoir exiger un code par e-mail plus tard (nouvel appareil, ou à chaque connexion) sans changer les contrats.
- **Date de réexamen** : avant le premier déploiement chez le commerce pilote, et au plus tard le **31/01/2027**.

## Conséquences et limites connues

- L'adresse e-mail du propriétaire devient son identifiant : un changement d'adresse n'est pas géré (hors lot). Qui contrôle la boîte e-mail contrôle le compte : le propriétaire doit protéger sa messagerie.
- Un propriétaire ne peut avoir qu'un commerce.
- Les demandes de vérification expirées ne sont pas encore purgées (tâche planifiée à venir) ; elles ne contiennent qu'un condensat.
- Le premier point de vente n'est pas créé par l'inscription : il le sera avec l'enrôlement du premier appareil.
- Le code par e-mail est recopié dans les journaux **uniquement** avec l'adaptateur de développement.

## Réexamen

À la date ci-dessus, au choix du serveur d'envoi, ou si un incident de compte propriétaire survient.

## Évolution du 23/10/2026 : e-mail à la place du SMS

Le porteur du projet a décidé de **ne pas utiliser de SMS ni WhatsApp** : pas de fournisseur à choisir ni à payer, et l'envoi se fait avec MailKit (SMTP).
La première version de cette décision (livrée dans la PR 10) identifiait le propriétaire par son numéro de téléphone et envoyait le code par SMS.

- **Identifiant du propriétaire** : l'adresse e-mail remplace le numéro de téléphone (domaine `EmailAddress`, annuaire `owner_directory`, table `email_verifications`, migration `UseEmailForOwners`).
- **Envoi** : `IEmailSender` ; adaptateur SMTP (`SmtpEmailSender`, MailKit) configuré par `Mail:Smtp` (`Host`, `Port`, `UserName`, `Password`, `FromAddress`, `FromName`, `Security`). Hors développement et tests, le serveur refuse de démarrer sans `Mail:Smtp:Host`.
  En développement, l'e-mail est écrit dans le journal de l'API.
- **Échec d'envoi** : l'utilisateur lit « Nous n'avons pas pu envoyer l'e-mail… » (503) et peut réessayer ; pour « mot de passe oublié », l'échec reste invisible afin de ne pas révéler qu'un compte existe.
- **Règles inchangées** : code à 6 chiffres, 10 minutes, 5 essais, 3 renvois, 3 demandes par adresse et par jour, mot de passe de 10 caractères au moins, verrouillage, dérogation « pas de code à chaque connexion ».
- **À prévoir avant le pilote** : un serveur SMTP et une adresse d'expédition sur le domaine du produit, avec SPF/DKIM pour que les codes n'arrivent pas dans les courriers indésirables. La délivrabilité se vérifie avec de vrais commerçants.
- **Migration** : refusée si des comptes identifiés par téléphone existent (aucun en production) ; en développement, repartir d'une base vide (`./scripts/dev.sh reset`).
