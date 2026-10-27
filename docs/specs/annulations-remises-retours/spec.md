# Spécification — Annulations, remises et retours

**Statut :** brouillon à valider ; décisions de `docs/decisions/0020-annulations-remises-et-retours.md`.
**Lots :** (1) serveur : retours, remboursements en caisse, stock, validation par PIN, migration ; (2) caisse : remise au paiement, « Ventes du jour », annulation, retour, tickets.

## Objectif

Corriger une erreur, accorder un geste commercial et reprendre un article **sans fausser le stock ni la caisse**, en laissant au patron une trace claire de qui a fait quoi.

## Exigences

### ARR-01 Annulation d'une vente entière
`POST /api/v1/sales/{id}/cancel` avec le motif et la **validation d'un gérant** (identifiant et PIN) quand l'appelant n'est pas gérant ou propriétaire. Une vente annulée ne peut plus l'être ni faire l'objet d'un retour. Le motif est obligatoire (liste proposée : « Erreur de saisie », « Le client renonce », « Article défectueux », « Autre » + texte libre). Refus (profil, PIN faux, vente déjà annulée) : journalisé.

### ARR-02 Retour partiel
`POST /api/v1/sales/{id}/returns` : lignes de la vente avec la quantité rendue (entier pour une pièce, millièmes pour un produit pesé), destination de chaque ligne (**remise en rayon** par défaut ou **perte**), motif, validation d'un gérant comme ci-dessus. Plusieurs retours successifs sur une même vente sont possibles ; pour chaque ligne, le cumul rendu ne dépasse pas la quantité vendue (422 « Vous rendez plus que ce qui a été vendu »). Une vente annulée n'a pas de retour.

### ARR-03 Montant remboursé
Pour une ligne : `arrondi(total de ligne × quantité rendue ÷ quantité vendue)` (franc, demi vers le haut), puis la remise de la vente est retirée **au prorata** des lignes rendues (le dernier retour solde l'arrondi pour que le cumul des retours ne dépasse jamais le net payé). Le serveur calcule et renvoie le montant ; le client ne le fournit pas.

### ARR-04 Répartition du remboursement
Le montant se répartit sur les moyens de paiement d'origine, dans l'ordre : **crédit** d'abord (la dette du client diminue), puis **espèces**, puis **Mobile Money** (par moyen, au prorata). La réponse détaille : « Rendre en espèces : X », « Dette effacée : Y », « À rembourser par Wave : Z ». Pas de remboursement supérieur à ce qui a été payé par chaque moyen.

### ARR-05 Effet sur la caisse
Le remboursement **en espèces** est enregistré comme une sortie du tiroir dans la **session ouverte de la caisse qui le fait** (jamais dans celle de la vente). `Attendu = fond + espèces encaissées − espèces remboursées`. Pas de session ouverte et part en espèces > 0 : 422 « Ouvrez la caisse pour rembourser en espèces. » (aucun effet). Le résumé et la clôture affichent « Remboursements en espèces ». Une annulation d'une vente de la **session encore ouverte** suit la même règle (le résultat net est le même).

### ARR-06 Effet sur le stock
Annulation et retour créent un mouvement par ligne : `SaleCancellation`/`Return` (remise en rayon : stock + quantité exacte) ou `Loss` avec le motif (perte). Un produit sans suivi de stock n'est pas touché. Le coût moyen n'est pas recalculé par un retour (il ne change qu'à la réception).

### ARR-07 Validation par PIN d'un gérant
Même mécanisme que ADR 0018 (identifiant + PIN, 5 essais puis 15 minutes de verrou, réponse identique pour compte inconnu et PIN faux). Un gérant ou le propriétaire connecté n'a rien à saisir. L'approbation est journalisée avec le nom du gérant. Réseau obligatoire.

### ARR-08 Remise à la vente (écran)
Au paiement, bouton « Remise » : montant en FCFA **ou** pourcentage (virgule ou point), le résultat en FCFA est affiché avant de valider ; le total net ne peut pas être nul. Jusqu'au seuil du commerce (10 %) : direct. Au-delà : fenêtre « Validation du gérant » (identifiant + PIN), le serveur tranche. **Sans réseau, au-delà du seuil** : « Cette remise demande l'accord d'un gérant : il faut du réseau. » La remise est ensuite envoyée avec la vente comme aujourd'hui (`discountFcfa`, `discountApproval`).

### ARR-09 Ventes du jour
Écran « Ventes » : liste des ventes des 7 derniers jours (n° de ticket, heure, caissier, total, état Validée / Annulée / Retour partiel), recherche par numéro de ticket, ouverture du détail. Caissier : ses ventes ; gérant et propriétaire : toutes. Réseau obligatoire (« Cet écran a besoin du réseau »). Boutons « Annuler la vente » et « Retour » dans le détail (profils qui encaissent).

### ARR-10 Écran d'annulation et de retour
Annulation : motif, choix « marchandise remise en rayon / abîmée » (global, modifiable ligne par ligne), validation du gérant si besoin, récapitulatif « Vous rendez 3 000 FCFA en espèces ». Retour : cases à cocher par ligne, quantité (pavé décimal pour le poids), destination, motif, même récapitulatif. Confirmation explicite avant d'envoyer ; le résultat dit **quoi faire** (« Rendez 3 000 FCFA au client », « Remboursez 1 500 FCFA par Wave »). Un envoi répété (double appui, coupure) ne rembourse jamais deux fois (identifiant d'opération fixé à l'ouverture de l'écran).

### ARR-11 Tickets
Le ticket d'une vente avec remise écrit la ligne « Remise −X FCFA » et le net. Un **ticket de retour/annulation** (imprimable) reprend le numéro du ticket d'origine, les articles rendus, le montant et le moyen de remboursement. La réimpression d'une vente annulée porte « ANNULÉ ».

### ARR-12 Journal et rapports
Actions : `sale.cancelled`, `sale.cancel.refused` (existantes), `sale.returned`, `sale.return.refused`, `sale.discount.approved/refused` (existantes). Le détail dit le ticket, le motif, le montant et le gérant qui a validé. Les rapports excluent les ventes annulées (déjà) et **déduisent les retours** du chiffre d'affaires et de la marge ; l'export ajoute une feuille « Retours ». L'état de la vente (« Retour partiel ») apparaît dans les listes.

### ARR-13 Sécurité et intégrité
- Toute opération est **rejouable** par identifiant (jamais deux remboursements) ; deux requêtes simultanées sur la même vente : l'une réussit, l'autre est refusée (jeton de version).
- Le commerce vient du jeton ; une vente d'un autre commerce est introuvable.
- Aucun montant ne vient du client : quantités et motifs seulement.
- Aucune opération sans réseau, aucune validation devinée localement.

## Scénarios

1. **Erreur de saisie.** Awa vend 3 savons au lieu de 2. Elle ouvre la vente dans « Ventes », touche « Annuler », choisit « Erreur de saisie » ; Kofi (gérant) tape son identifiant et son PIN ; l'écran dit « Rendez 1 500 FCFA au client » ; le stock revient ; la caisse attend 1 500 FCFA de moins.
2. **Retour partiel.** Un client rapporte 1 des 3 boîtes de lait achetées avec une remise de 10 % : remboursement = prix de la boîte − sa part de remise.
3. **Riz au poids.** Retour de 0,500 kg sur 2,000 kg : stock + 500 g, remboursement proportionnel arrondi au franc.
4. **Article abîmé.** Le gérant choisit « perte » : le stock ne revient pas, une perte est inscrite avec le motif.
5. **Payé par Wave.** L'écran dit « À rembourser par Wave : 4 000 FCFA » ; rien ne sort du tiroir.
6. **Caisse fermée.** Remboursement en espèces sans session ouverte : refusé avec « Ouvrez la caisse… ».
7. **Coupure du réseau.** Les boutons Annuler et Retour expliquent qu'il faut du réseau, sans rien tenter.
8. **Remise de 25 %.** Le caissier saisit 25 % : fenêtre de validation ; PIN faux (« Identifiant ou code PIN incorrect. ») ; puis le bon PIN : la vente passe, le journal note « Remise de 2 500 FCFA approuvée par Kofi ».
9. **Double appui.** Deux envois du même retour : un seul remboursement.
10. **Retour excessif.** Rendre 3 articles alors que 2 restent à rendre : 422.

## Hors lot

Avoir, échange, remise par ligne, promotions, retour sans ticket, remboursement Mobile Money automatique, limite de durée, rapport par caissier (décision suivante : clôture à l'aveugle et rapport du soir).

## Plan d'essai (avant le code)

- **Domaine** : montant d'un retour (arrondis, remise au prorata, cumul, pièces et poids), répartition crédit/espèces/Mobile Money, session (attendu avec remboursements), statut de la vente.
- **Application** : validation par PIN (gérant, propriétaire, caissier sans PIN, verrouillage), stock (rayon/perte/sans suivi), session fermée, rejouabilité, journal.
- **Intégration** : API, concurrence de deux retours, isolation entre commerces, migration, rapports et export.
- **Front** : remise (FCFA, %, seuil, hors réseau), liste et détail des ventes, écrans annulation et retour, tickets.
- **Navigateur** : parcours complet sur pile isolée (vente, remise, retour partiel, annulation, clôture avec le bon attendu).
