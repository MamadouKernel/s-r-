# 0005 — Inscription, premier propriétaire, vérification du téléphone et SMS

**Statut :** accepté le 23/10/2026 (décisions du porteur du projet). Contient **une dérogation au cahier des charges**, motivée ci-dessous.

## Contexte

Aucun commerce réel ne peut être créé : la table `commerce` n'existe pas encore (les commerces ne sont que des identifiants dans les autres tables), aucun point d'entrée public ne crée un premier propriétaire
(ADR 0003), et une caisse se rattache en saisissant un identifiant de 36 caractères. Le cahier des charges prévoit « mot de passe et code par SMS » pour le propriétaire (section 12) et une inscription par téléphone avec essai gratuit
(section 11, durée à définir).

## Décisions

1. **Inscription en deux temps, sans rien créer avant la vérification.** Une demande (`phone_verifications`) porte les informations saisies et un code SMS ; le commerce, le propriétaire et son entrée d'annuaire
   ne sont créés qu'à la finalisation, dans une seule transaction.
2. **Un propriétaire par numéro de téléphone** (numéro ivoirien +225, 10 chiffres). Une table d'annuaire sans sécurité par ligne (`owner_directory`: numéro → commerce) permet à un propriétaire de se connecter sans connaître
   l'identifiant de son commerce ; elle ne contient aucun secret. Le commerce lui-même (`commerces`) reste protégé par la sécurité par ligne.
3. **Le propriétaire se connecte par numéro et mot de passe.** *Dérogation* : le cahier ajoute un code SMS ; il n'est pas demandé à la connexion (voir ci-dessous).
4. **Mot de passe** : 10 à 128 caractères, au moins une lettre et un chiffre, différent du numéro. Même condensat que le PIN (HMAC avec le secret du serveur puis PBKDF2 salé, 600 000 itérations).
5. **Code SMS** : 6 chiffres aléatoires, 10 minutes, 5 essais, 3 renvois espacés de 60 secondes, 3 demandes par numéro et par jour ; stocké sous forme de condensat lié à la demande, jamais en clair, jamais renvoyé par l'API.
6. **Passerelle SMS** : un contrat `ISmsSender` et un adaptateur de développement qui écrit le message dans les journaux. Le fournisseur réel se choisit avant le pilote. **Le serveur refuse de démarrer hors développement
   et hors tests tant qu'aucun fournisseur réel n'est configuré** (`Sms:Provider`), pour qu'aucun code ne soit « envoyé » dans le vide.
7. **Essai gratuit de 30 jours**, formule « Commerce » ; la date de fin est enregistrée, la mise en lecture seule (RG-14) viendra avec l'abonnement.
8. **Mot de passe oublié** : réponse identique pour un numéro connu ou inconnu ; un code SMS valide permet de définir un nouveau mot de passe, ce qui lève le verrouillage et est tracé.
9. **Conditions d'utilisation** : l'acceptation (version et date) est enregistrée avec le commerce. Le texte juridique est fourni par le porteur du projet ; la version courante est une configuration (`Signup:TermsVersion`).
10. **Appareils (décidé, livré en PR 2 et 3)** : enrôlement par lien ou QR à usage unique, appareils révocables, la révocation coupant l'accès immédiatement.

## Dérogation : pas de code SMS à la connexion du propriétaire

- **Motivation** : un SMS à chaque connexion coûte de l'argent et dépend de la livraison du réseau mobile ; le propriétaire se connecte souvent, depuis son téléphone.
- **Périmètre** : la connexion du propriétaire seulement. Le PIN des caissiers, le SMS d'inscription et le SMS de réinitialisation ne changent pas.
- **Risque** : un mot de passe volé ou deviné donne accès à toutes les ventes, au stock et à l'équipe du commerce. Il n'y a pas de second facteur.
- **Compensations** : mot de passe d'au moins 10 caractères, verrouillage (5 échecs, 15 minutes), limitation de débit par adresse, réponses indiscernables (pas de fuite sur les comptes), journal d'audit, réinitialisation par SMS.
  La connexion est écrite de façon à pouvoir exiger un code SMS plus tard (nouvel appareil, ou à chaque connexion) sans changer les contrats.
- **Date de réexamen** : avant le premier déploiement chez le commerce pilote, et au plus tard le **31/01/2027**.

## Conséquences et limites connues

- Le téléphone du propriétaire devient un identifiant : un changement de numéro n'est pas géré (hors lot).
- Un propriétaire ne peut avoir qu'un commerce.
- Les demandes de vérification expirées ne sont pas encore purgées (tâche planifiée à venir) ; elles ne contiennent qu'un condensat.
- Le premier point de vente n'est pas créé par l'inscription : il le sera avec l'enrôlement du premier appareil.
- Le code SMS est recopié dans les journaux **uniquement** avec l'adaptateur de développement.

## Réexamen

À la date ci-dessus, au choix du fournisseur SMS, ou si un incident de compte propriétaire survient.
