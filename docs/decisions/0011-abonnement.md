# 0011 — Abonnement après l'essai

**Statut :** accepté le 25/10/2026 (quatre choix du porteur du projet, tous alignés sur la recommandation). Applique la règle RG-14 de `docs/analyse-merise.md`.

## Contexte

Chaque boutique reçoit 30 jours d'essai (`Commerce.TrialEndsAt`), mais rien ne se passe à l'échéance : ni abonnement, ni paiement, ni rappel, ni restriction. Il faut pouvoir encaisser l'abonnement, sans bloquer brutalement un commerce qui vit de sa caisse.

## Décisions

1. **Une seule formule par boutique**, caisses et utilisateurs illimités. Deux périodes : **mensuelle**, **annuelle** (12 mois pour le prix de 10). Le prix est un **paramètre de configuration**, à fixer par le porteur du projet avant la mise en service ; aucun montant n'est écrit dans le code.
2. **Paiement à la main au début.** Le propriétaire paie en Mobile Money (Wave, Orange Money, MTN MoMo, Moov Money) vers le numéro de Sôrô, puis **déclare** le paiement dans l'application avec la **référence** de la transaction. L'équipe Sôrô **valide** après avoir vérifié sur son relevé. Pas d'agrégateur ni de compte marchand pour l'instant ; l'écran et le modèle de données ne dépendent pas du canal, pour brancher un agrégateur plus tard sans tout refaire.
3. **Paiement à chaque période, pas de prélèvement automatique** : seuls Orange Money et MTN MoMo savent prélever, pas Wave, le plus utilisé.
4. **Validation par un outil d'exploitation, pas par un écran public.** Valider un paiement touche tous les commerces ; l'API des commerces n'a pas le droit de le faire (isolation RG-15). Un outil en ligne de commande réservé à l'équipe, qui se connecte avec le rôle propriétaire de la base, valide ou refuse les paiements déclarés. Un écran d'administration pourra venir quand le volume le demandera.
5. **États dérivés de dates, jamais stockés** : *essai* → *actif* (payé) → *grâce* (7 jours après l'échéance) → *lecture seule*. Pas d'état à synchroniser ni de tâche qui puisse l'oublier ; le calcul se fait à chaque lecture.
6. **Rappels par e-mail** à J-7, J-3 et le jour de l'échéance (essai et abonnement), une seule fois chacun (table des rappels envoyés).
7. **Lecture seule sans perdre une vente** : les ventes déjà faites hors ligne se synchronisent **toujours** (elles ont eu lieu ; les rejeter ne rendrait pas la marchandise), comme pour le plafond de crédit (0008) ; elles sont signalées dans le journal. Seules les **nouvelles** opérations en ligne sont refusées. Les données restent consultables et **exportables** ; elles sont conservées **12 mois** après l'échéance.
8. **Reprise immédiate** : un paiement validé réactive tout aussitôt, à partir du dernier jour payé (pas de jours perdus ni offerts).

## Risques et limites

- Le travail de validation est manuel : acceptable pour les premiers dizaines de commerces ; au-delà, brancher un agrégateur.
- Une référence Mobile Money fausse est la fraude à surveiller : la validation se fait sur le relevé de Sôrô, jamais sur la seule déclaration, et une référence ne sert qu'une fois (même règle que les ventes).
- Une caisse longtemps hors ligne ne connaît pas l'état d'abonnement : elle continue de vendre jusqu'à son prochain contact avec le serveur ; l'écart est borné par le délai de synchronisation.
