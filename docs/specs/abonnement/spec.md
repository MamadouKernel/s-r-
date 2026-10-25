# Spécification — Abonnement après l'essai

**Statut :** brouillon à valider ; décisions de `docs/decisions/0011-abonnement.md`. Règles : RG-14 (impayé), RG-15 (isolation), RG-17 (rejeu). Domaine D6 de l'analyse.
**Lots :** (1) serveur : états, paiement déclaré et validé, rappels, restriction ; (2) écrans : bandeau d'état, « Mon abonnement », caisse en lecture seule ; (3) export des données du propriétaire.

## Objectif

Après l'essai de 30 jours, la boutique doit s'abonner. Le propriétaire sait **où il en est** (jours restants, échéance), **paie** par Mobile Money et **déclare** son paiement ; l'équipe Sôrô le valide. Sans paiement, la boutique passe en lecture seule après 7 jours de grâce, **sans perdre aucune vente** ni aucune donnée.

## Exigences

### ABO-01 Les états
L'état d'une boutique est **calculé** à partir de ses dates, à chaque lecture (jamais stocké) :
- **Essai** : avant `TrialEndsAt` et sans abonnement payé.
- **Actif** : jusqu'à la fin de la période payée (`PaidUntil`).
- **Grâce** : les **7 jours** qui suivent la fin de l'essai ou de la période payée ; tout fonctionne, un bandeau prévient.
- **Lecture seule** : au-delà. Les données sont conservées 12 mois.
Un paiement validé pendant la grâce ou la lecture seule redonne l'état **Actif** aussitôt.

### ABO-02 Formule et prix
Une formule unique : **mensuelle** (1 mois) ou **annuelle** (12 mois au prix de 10). Les prix sont des **paramètres de configuration** (`Subscription:MonthlyPriceFcfa`, `Subscription:AnnualPriceFcfa`) ; l'application refuse de démarrer en production sans eux (même garde-fou que le courrier).

### ABO-03 Lire son abonnement
`GET /api/v1/subscription` (propriétaire, gérant ; caissier : état seul) renvoie : état, jours restants avant la prochaine échéance, date d'échéance, prix des deux périodes, moyens de paiement acceptés avec le **numéro de Sôrô** pour chacun, paiements déclarés et leur statut. Le **statut seul** est aussi envoyé à toutes les caisses avec le catalogue, pour afficher le bandeau.

### ABO-04 Déclarer un paiement
`POST /api/v1/subscription/payments` (propriétaire seulement) : identifiant du paiement (rejouable), **période** (mensuelle ou annuelle), **moyen** (Wave, Orange Money, MTN MoMo, Moov Money), **référence** de la transaction (obligatoire), montant versé. Le montant doit être **au moins** le prix de la période. Une référence ne sert **qu'une fois** pour un même moyen, tous commerces confondus (une fraude à la référence est détectée). Le paiement est **en attente de validation**. Un paiement déclaré en attente n'active rien.

### ABO-05 Valider ou refuser (outil d'exploitation)
Un **outil en ligne de commande** de l'équipe Sôrô liste les paiements en attente (commerce, période, moyen, référence, montant, date) et les **valide** ou les **refuse** (motif obligatoire). Il se connecte avec le rôle propriétaire de la base ; l'API des commerces ne peut pas le faire. Valider **prolonge** `PaidUntil` de la période, à partir du **plus tard** entre aujourd'hui et la fin de la période déjà payée ; refuser prévient le propriétaire par e-mail avec le motif. Chaque validation ou refus est tracé (journal d'exploitation) et envoie un e-mail de confirmation.

### ABO-06 Rappels par e-mail
À **J-7, J-3 et J** de l'échéance (fin d'essai ou de période payée) : un e-mail au propriétaire, en français simple, qui dit combien de jours restent, le prix et **comment payer**. Une table des rappels envoyés garantit **un seul e-mail par échéance et par rappel**, même si la tâche tourne plusieurs fois. À la **fin de la grâce** : un dernier e-mail qui dit que la boutique est en lecture seule et que rien n'est perdu.

### ABO-07 Restriction en lecture seule
En **lecture seule**, le serveur refuse avec le code `subscription_expired` (403) toute opération d'**écriture** en ligne sauf : se connecter, déclarer un paiement, lire, exporter. Sont **toujours acceptées** : la synchronisation de ventes, d'ouvertures et de clôtures de caisse **déjà faites hors ligne** (elles ont eu lieu) ; chacune est signalée dans le journal (`subscription.expired.sale`) pour que le propriétaire voie ce qui s'est passé. Pendant l'**essai**, l'**actif** et la **grâce** : aucune restriction.

### ABO-08 Cohérence et droits
Un autre commerce ne voit ni l'abonnement, ni les paiements d'un commerce. Le caissier et le magasinier ne voient que l'état. Une référence déjà utilisée est un 409 clair. Rejouer une déclaration avec le même identifiant ne crée rien de plus.

### ABO-09 Écrans (lot 2)
- **Bandeau** sur tous les écrans (propriétaire, gérant) : « Votre essai finit dans 5 jours », « Votre abonnement se termine demain », « Abonnement échu : plus que 3 jours avant la lecture seule », « Lecture seule : vos données sont conservées. Payez pour reprendre ». Le caissier voit un bandeau court sans prix.
- **Mon abonnement** (propriétaire) : état en toutes lettres, échéance, **comment payer** étape par étape (« 1. Envoyez X FCFA au numéro Wave … 2. Notez la référence reçue par SMS … 3. Saisissez-la ici »), formulaire de déclaration, historique des paiements avec leur statut (« En attente de validation », « Validé », « Refusé : motif »).
- **Caisse en lecture seule** : « Encaisser » est désactivé avec l'explication et un lien vers « Mon abonnement » pour le propriétaire ; pour le caissier : « Prévenez votre responsable ». Une caisse hors ligne continue de vendre jusqu'à sa prochaine synchronisation.

### ABO-10 Export des données (lot 3)
Le propriétaire télécharge à tout moment, **même en lecture seule**, un classeur Excel : produits (avec familles et formats), clients et dettes, ventes de la période choisie. Les formules ne sont jamais écrites comme formules (un texte qui commence par `=` est écrit comme texte).

## Scénarios d'acceptation

1. **Essai.** Jour 25 : le bandeau dit « Votre essai finit dans 5 jours » ; tout fonctionne.
2. **Rappels.** J-7, J-3 et J : un e-mail chacun, jamais deux fois ; ceux d'un commerce déjà payé ne partent pas.
3. **Paiement.** Le propriétaire déclare 10 000 FCFA mensuel, Wave, référence « W-123 » : statut « En attente » ; l'équipe valide : l'échéance avance d'un mois, un e-mail confirme.
4. **Paiement en avance.** Valider un mois alors qu'il reste 10 jours : la nouvelle échéance est dans 1 mois et 10 jours.
5. **Référence déjà utilisée** par un autre commerce : refusée (409).
6. **Montant insuffisant** : refusé avec le prix attendu.
7. **Grâce.** Jour 31 à 37 : tout fonctionne, bandeau d'avertissement.
8. **Lecture seule.** Jour 38 : créer un produit, ouvrir une caisse, enregistrer une vente en ligne : 403 `subscription_expired`. Lire, exporter, déclarer un paiement : acceptés.
9. **Vente hors ligne.** Une caisse hors ligne a vendu pendant la lecture seule : à la reconnexion, ses ventes sont **enregistrées** et signalées dans le journal.
10. **Reprise.** Le paiement validé en lecture seule : plus aucune restriction, à la seconde.
11. **Refus.** Paiement refusé avec motif : le propriétaire reçoit l'e-mail et voit « Refusé : motif » ; il peut redéclarer.
12. **Droits et isolation.** Le caissier ne déclare rien ; le commerce B ne voit pas les paiements du commerce A.
13. **Rejeu.** Déclarer deux fois avec le même identifiant : un seul paiement.

## Hors lot

- Agrégateur de paiement, lien de paiement et confirmation automatique ; prélèvement récurrent ; factures PDF et TVA ; code promo et parrainage ; remboursement ; changement de formule.
- Écran d'administration web pour l'équipe Sôrô.
- Suppression définitive des données après 12 mois (politique de conservation, à traiter avec l'hébergement).
