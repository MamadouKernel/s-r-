# Spécification — Clôture à l'aveugle, sorties d'espèces et rapport du soir

**Statut :** brouillon à valider, **réécrit le 27/10/2026 à partir des pratiques documentées du commerce de détail** (voir la décision 0021, qui cite les sources et dit ce qui n'a pas pu être établi) ; décisions de `docs/decisions/0021-cloture-a-l-aveugle-sorties-d-especes-et-rapport-du-soir.md`.
**Lots :** (1) serveur : mouvements de caisse, clôture à deux comptages, signalement des écarts et des caisses ouvertes, rapport du jour, e-mail du soir ; (2) caisse : mouvements, clôture à l'aveugle avec aide au comptage, page « Rapport du jour ».

## Objectif

Que le patron sache chaque soir **si l'argent est dans le tiroir et qui a fait quoi**, sans que le caissier puisse ajuster son comptage à ce qu'on lui demande, et sans qu'une dépense honnête crée un faux manque.

## Exigences

### CLO-01 Mouvements de caisse
`POST /api/v1/register-sessions/{id}/movements` : `{ movementId, kind, amountFcfa, reason, at, approvalToken? }`. `kind` : `Expense` (dépense), `Drop` (versement au coffre), `Topup` (apport de monnaie). Motif obligatoire pour une dépense (liste : « Transport », « Sachets et emballages », « Eau ou électricité », « Repas », « Autre » + texte libre) ; facultatif ensuite. Montant entier > 0. Rattaché à la **session ouverte** (404/422 sur une session close). Rejouable par `movementId`. Réservé aux profils de caisse, sur leur propre session (un gérant sur toutes).

### CLO-02 Montant attendu
`attendu = fond + espèces encaissées − espèces remboursées − dépenses − versements + apports` (RG-24). Un mouvement qui rendrait le tiroir négatif est refusé (« Cette sortie dépasse l'argent qui doit se trouver dans le tiroir »). Le serveur recalcule ; le client n'envoie jamais de total.

### CLO-03 Validation des grosses sorties
Une `Expense` ou un `Drop` de plus de **5 000 FCFA** exige l'approbation d'un gérant (preuve signée, usage `cash-out`, liée à `movementId` et au montant, comme `docs/decisions/0020`). Un gérant ou le propriétaire qui saisit lui-même n'a rien à prouver. Refus : 403 et journal `cash.movement.refused`.

### CLO-04 Clôture à deux comptages
`POST /api/v1/register-sessions/{id}/close` accepte `{ countedCashFcfa, at, recount? , denominations? }`.
- Premier envoi : si `|compté − attendu| ≤ tolérance` (500 FCFA par défaut) → session close, écart enregistré. Sinon → **réponse 200 `{ needsRecount: true }` sans montant, sans sens, sans l'attendu**, session toujours ouverte, premier comptage conservé.
- Second envoi (`recount: true`) : définitif, quel que soit l'écart. Les deux comptages sont gardés.
- Un envoi rejoué avec le même comptage rend le même résultat ; un autre comptage sur une session close est refusé (comme aujourd'hui).
- `denominations` : détail facultatif `{ "10000": 2, "500": 3, … }` dont la somme doit égaler `countedCashFcfa`.
- **Le serveur ne renvoie ni l'attendu ni l'écart à un caissier** ; il les renvoie au gérant et au propriétaire.
- La clôture envoyée **hors ligne** suit la même règle : le comptage est gardé, le serveur répond au retour du réseau (voir CLO-09).

### CLO-05 Écart signalé
Écart au-delà de la tolérance : inscription au journal (`session.variance`, « Écart de caisse de −1 500 FCFA (session de Awa) »), visible dans `GET /register-sessions?onlyWithVariance=true` et le rapport du jour. L'écart n'est jamais modifiable.

### CLO-06 Caisse oubliée ouverte
`GET /register-sessions?status=Open` renvoie `openForHours` ; une session ouverte depuis plus de **14 heures** est marquée « à clôturer ». Le tableau de bord et le rapport du jour l'indiquent. Rien ne la ferme de force.

### CLO-07 Rapport du jour
`GET /api/v1/reports/day?date=YYYY-MM-DD` (propriétaire, gérant, comptable ; défaut : aujourd'hui) :
- **Ventes** : total net, nombre de tickets, panier moyen, par moyen de paiement.
- **Par caissier** : ventes (nombre, total), annulations (nombre, montant rendu), retours (nombre, montant), remises (nombre, montant), écart de caisse de ses sessions closes.
- **Sorties d'espèces** : total par type et par motif, avec qui et quand. **Mobile Money** : total par opérateur (à retrouver sur le téléphone du patron : cet argent n'est pas dans le tiroir).
- **Sessions** : fond, attendu, compté, écart, état ; les non clôturées en tête.
- **Crédit** : dettes créées, versements encaissés. **Stock** : produits en rupture ou sous le seuil (pour qui lit le stock).
La journée est le jour UTC (Côte d'Ivoire : même chose). Le rapport ne contient aucun prix d'achat ni marge pour le comptable.

### CLO-08 E-mail du soir
`soro-ops rapport-du-soir [--date]` (à planifier chaque jour à 20 h) envoie à chaque commerce actif un e-mail au propriétaire (le comptable, qui n'a pas d'adresse dans Sôrô, lit la page « Rapport du jour ») avec le contenu du rapport en texte simple, en français, montants en FCFA, **un seul e-mail par commerce et par jour** (rejouable sans doublon). Aucun envoi pour un commerce sans vente ni caisse ouverte ce jour-là, ou en lecture seule. Échec d'envoi : compté et journalisé, jamais bloquant.

### CLO-09 Caisse : clôture à l'aveugle
Écran « Clôturer la caisse » : le caissier voit seulement « Comptez les espèces du tiroir », un champ de montant et « Compter par billets » (aide facultative : une ligne par coupure, somme en direct). **Ni le montant attendu, ni l'écart, ni les ventes en espèces ne s'affichent**. Gérant et propriétaire voient l'attendu **après** avoir saisi leur comptage ; un gérant peut compter à la place du caissier. Si le serveur demande un recomptage : « Votre comptage ne correspond pas à ce qui est attendu. Recomptez, billet par billet, puis confirmez. » (aucun montant). Après la clôture : « Caisse clôturée. Montant compté : X FCFA. » Hors réseau : le recomptage est décidé **sur l'appareil**, qui connaît l'attendu local (fond + espèces − remboursements − sorties + apports) : le caissier recompte une fois, puis le second comptage est gardé et envoyé. Si le serveur répond quand même `needsRecount` (ventes d'un autre appareil reçues entre-temps), l'appareil renvoie le même comptage en second comptage final, sans déranger le caissier : le comptage de la fermeture est celui qu'il a confirmé.

### CLO-10 Caisse : mouvements
Bouton « Mouvements » dans la caisse : choix Dépense / Versement au coffre / Apport, montant, motif (liste), enregistrement. Au-delà de 5 000 FCFA : fenêtre « Validation du gérant » (réseau obligatoire). Les mouvements du jour s'affichent (heure, type, montant, motif) ; un mouvement erroné s'annule par un mouvement inverse motivé. Hors réseau : les petites sorties sont gardées dans la file et envoyées **avant** la clôture, dans l'ordre. Le ticket de clôture ne montre pas l'attendu.

### CLO-11 Page « Rapport du jour »
Dans l'application (propriétaire, gérant, comptable) : la même matière que l'e-mail, avec choix de la date, dates précédentes en lecture seule, « besoin du réseau » sinon. Vocabulaire simple : jamais « variance », « session » : « écart de caisse », « caisse de Awa ».

### CLO-13 Avertissement « trop d'argent dans le tiroir »
Quand l'attendu de la session dépasse **100 000 FCFA** (valeur de départ), la caisse affiche « Faites un versement au coffre » avec un bouton vers « Mouvements ». Aucun blocage dans ce lot. Le dépassement figure dans le rapport du soir.

### CLO-14 Écart sans retenue
Aucun écran, aucun rapport ni aucun e-mail ne calcule une retenue, une dette ou un remboursement du caissier ; l'écart est un fait à examiner.

### CLO-12 Sécurité et intégrité
- Aucun montant attendu, écart ni détail d'un autre commerce n'est lisible ; le commerce vient du jeton.
- Un caissier ne peut lire ni l'attendu ni l'écart par aucune route (liste de sessions, détail, rapport).
- Tout mouvement et tout comptage sont rejouables par identifiant ; deux requêtes simultanées sur la même session : une seule réussit (jeton de version).
- Chaque refus et chaque écart sont journalisés avec l'auteur.

## Situations types

Elles viennent des pratiques documentées, pas d'une boutique observée : les montants sont des **ordres de grandeur**, à remplacer par ceux du pilote.

1. **Soir ordinaire.** Le caissier compte les coupures sans rien voir ; la caisse est close ; le rapport du soir dit « écart 0 » pour sa caisse.
2. **Dépense honnête.** Une petite dépense payée du tiroir (transport, sachets) est saisie avec son motif : à la clôture l'attendu en tient compte, elle n'apparaît pas comme un manque.
3. **Trop d'argent dans le tiroir.** Passé le seuil, la caisse avertit « Faites un versement au coffre » ; le gérant prend l'argent, valide avec son PIN, et le versement est noté à part d'une dépense.
4. **Erreur de comptage.** Un billet oublié : « Recomptez… » (sans dire de combien ni dans quel sens) ; au second comptage, le compte est juste : clôture, aucun signalement.
5. **Vrai écart.** Le second comptage donne le même résultat, hors tolérance : clôture ; le journal note l'écart, **manque ou excédent**, avec le caissier et le jour ; le rapport du soir le met en tête. Aucune retenue n'est calculée.
6. **Caisse oubliée.** Un caissier n'a pas clôturé : le rapport du soir et le tableau de bord le disent.
7. **Coupure du réseau à la clôture.** Le caissier clôture sans réseau ; au retour du réseau, un recomptage est demandé : la caisse l'invite à recompter.
8. **Caissier curieux.** Aucune route ne lui rend l'attendu ; l'écran n'affiche rien d'autre que ce qu'il a compté.
9. **Rejeu.** Le même mouvement envoyé deux fois : une seule dépense.
10. **Sortie trop grosse pour le tiroir.** Refusée avec un message qui dit quoi faire.

## Hors lot

Photo du justificatif, WhatsApp et SMS, heure d'envoi propre à chaque commerce, inventaire surprise, rapprochement bancaire, plusieurs tiroirs.

## Plan d'essai (avant le code)

- **Domaine** : attendu avec dépenses, versements, apports et remboursements ; refus d'un attendu négatif ; clôture à deux comptages, tolérance, comptage définitif, écart figé ; somme des coupures.
- **Application** : validation des grosses sorties (gérant, propriétaire, caissier sans accord, preuve d'un autre mouvement ou d'un autre montant), rejouabilité, droits (un caissier ne voit jamais attendu ni écart), journal des écarts, caisse ouverte depuis plus de 14 h, rapport du jour (par caissier, sorties, sessions, crédit), e-mail du soir (un par commerce et par jour, aucun envoi sans activité).
- **Intégration** : API, concurrence de deux mouvements, isolation entre commerces, migration, rapport et e-mail.
- **Front** : clôture à l'aveugle (rien d'affiché, recomptage, billets), mouvements, validation d'un gérant, page du rapport.
- **Navigateur** : parcours complet sur pile isolée (dépense, clôture à l'aveugle avec recomptage, rapport du jour).
