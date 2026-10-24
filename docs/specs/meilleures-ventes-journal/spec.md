# Spécification : meilleures ventes et journal des actions sensibles

**Statut :** brouillon ; deux fonctions V1 du cahier des charges (sections 8 et 10) qui complètent le tableau de bord.
Chacune a un endpoint serveur, puis un écran. Les maquettes ne sont pas validées avec le pilote (T030).

## Décisions

- **Meilleures ventes : trois classements** : les plus vendus (quantité), ceux qui rapportent le plus de chiffre d'affaires, et les plus rentables (marge).
- **La marge est une estimation, et le dit** : une ligne de vente fige son prix de vente mais pas le coût. La marge est calculée avec le **prix d'achat actuel** de la fiche
  (prix de vente figé − prix d'achat actuel) × quantité. Le prix d'achat *moyen* (RG-11) n'existait pas à l'origine : voir `docs/specs/cout-moyen/spec.md`, qui rend la marge exacte pour les ventes enregistrées depuis (coût retenu à la vente) et la signale « estimée » pour les plus anciennes.
  Un produit sans fiche (ou sans prix d'achat connu) n'a pas de marge plutôt qu'une marge fausse.
- **Droits de la marge** : seuls les profils qui voient les prix d'achat (propriétaire, gérant) peuvent classer par marge ou la lire ; un comptable reçoit 403 pour ce classement.
- **Le chiffre d'affaires d'un produit est la somme de ses lignes** (prix figé × quantité), **avant** une éventuelle remise accordée sur l'ensemble de la vente : la remise globale n'est pas ventilée par produit.
- **Les ventes annulées ne comptent jamais**, ni dans les quantités, ni dans le chiffre d'affaires, ni dans la marge.
- **Journal : propriétaire et comptable seulement** (surveillance et lecture). C'est un journal de contrôle : il trace aussi les actions du gérant. À confirmer avec le pilote.
- **Le journal est en lecture seule** et déjà alimenté par le serveur (annulations, remises, changements de fiche produit, pertes de stock, désactivations). Les créations de comptes
  et les changements de prix d'achat à la réception n'y figurent pas encore (hors lot, listé en bas).
- **Annuaire élargi au comptable** : pour afficher le nom des auteurs, le comptable peut lire l'annuaire (identifiant et nom seulement), comme le propriétaire, le gérant et le magasinier.
- Périodes en `[début, fin[` UTC, au plus 366 jours, comme les autres rapports.

## Exigences

### MV-01 Classement des produits (serveur)
`GET /api/v1/reports/top-products?from=&to=&by=&limit=` : `by` vaut `quantity` (défaut), `revenue` ou `margin` ; `limit` de 1 à 50 (défaut 10).
Chaque ligne : identifiant du produit, nom, quantité vendue, chiffre d'affaires, marge estimée (ou `null`). Tri décroissant sur le critère, puis par nom pour départager.
Ventes validées de la période seulement. Propriétaire, gérant et comptable ; `by=margin` : propriétaire et gérant. Commerce du jeton uniquement.

### MV-02 Journal (serveur)
`GET /api/v1/audit-log?from=&to=&category=&limit=&offset=` : entrées du plus récent au plus ancien ; chacune : identifiant, date, auteur, action, ressource, détail.
`category` : `sales` (remises et annulations), `catalogue` (fiches produit et prix), `stock` (pertes), `team` (comptes) ; omise : toutes. `limit` de 1 à 200 (défaut 50),
`offset` ≥ 0. La réponse indique s'il y a une suite (`hasMore`). Propriétaire et comptable ; les autres profils reçoivent 403. Commerce du jeton uniquement.

### MV-03 Annuaire du comptable (serveur)
`GET /api/v1/users/directory` accepte aussi le comptable.

### MV-04 Bloc « Meilleures ventes » du tableau de bord
Trois classements au choix (plus vendus, plus de chiffre d'affaires, plus rentables : ce dernier seulement pour le propriétaire et le gérant) et trois périodes (aujourd'hui, 7 jours, 30 jours).
Les cinq premiers, avec la quantité, le chiffre d'affaires et, pour la marge, la mention « estimée d'après le prix d'achat actuel ». État vide : « Aucune vente sur cette période ».
Un échec de ce bloc n'affecte pas les autres.

### MV-05 Page Journal
Réservée au propriétaire et au comptable. Filtres par catégorie et par période (aujourd'hui, 7 jours, 30 jours). Tableau : date et heure, action en français, auteur (nom, sinon identifiant court),
détail ; « Charger plus » quand le serveur indique une suite. États : chargement, vide, erreur, hors ligne ; défile dans son propre conteneur sur téléphone.

## Scénarios d'acceptation

- **SC-M1** Trois produits vendus : le classement par quantité, par chiffre d'affaires et par marge donne trois ordres différents et corrects.
- **SC-M2** Une vente annulée n'apparaît dans aucun classement.
- **SC-M3** Un comptable obtient le classement par quantité et par chiffre d'affaires, et un 403 pour la marge ; un caissier obtient un 403.
- **SC-M4** Un produit sans prix d'achat connu a une marge `null`, jamais zéro ni fausse.
- **SC-M5** Un autre commerce ne voit ni ventes, ni produits, ni entrées de journal du premier.
- **SC-M6** Le journal est paginé : 120 entrées, deux pages de 50 et une de 20, `hasMore` juste, sans doublon ni trou.
- **SC-M7** Le filtre « sales » ne renvoie que remises et annulations ; une période inversée ou une catégorie inconnue est refusée (422).
- **SC-M8** Un gérant, un caissier et un magasinier reçoivent 403 sur le journal.
- **SC-M9** La page Journal affiche le nom de l'auteur et l'action en français ; le comptable voit les noms grâce à l'annuaire.

## Hors lot

Créations de comptes et changements de prix d'achat à la réception dans le journal, prix d'achat moyen (RG-11) et marge exacte, comparaison de périodes pour les classements, export,
classement par catégorie, ventilation des remises globales par produit.
