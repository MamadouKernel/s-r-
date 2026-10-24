# Spécification — Prix d'achat moyen et marge exacte

**Statut :** brouillon ; règle RG-11 de `docs/analyse-merise.md` (« prix d'achat moyen recalculé à chaque réception »), décisions dans `docs/decisions/0007-cout-moyen-pondere.md`.

## Objectif

Aujourd'hui, saisir un prix d'achat à la réception **écrase** le prix de la fiche, et la marge du classement des meilleures ventes est calculée avec le prix d'achat **du jour** : elle se trompe dès que les prix des fournisseurs bougent.
Le commerçant veut savoir ce qu'il gagne vraiment : le prix d'achat devient un **prix moyen pondéré**, et chaque vente retient le coût du moment.

## Exigences

### CM-01 Prix d'achat moyen à la réception
Une réception peut indiquer le **prix d'achat unitaire de la livraison** (facultatif). Si elle le fait, le prix d'achat du produit devient la moyenne pondérée :
`nouveau = arrondi((stock utile × prix actuel + quantité reçue × prix de la livraison) ÷ (stock utile + quantité reçue))`
où le **stock utile** est le stock en rayon s'il est positif, 0 sinon (un stock négatif vient de ventes hors ligne passées avant la réception ; ces ventes ont déjà leur coût). Arrondi à l'FCFA le plus proche (0,5 vers le haut). Sans prix de livraison, le prix moyen ne change pas.
La modification manuelle du prix d'achat sur la fiche produit reste possible : elle **remplace** le prix moyen (correction ou point de départ) et est tracée comme toute modification de fiche.

### CM-02 Coût figé à la vente
Chaque ligne de vente retient, au moment où le serveur l'enregistre, le **prix d'achat moyen du produit** (« coût unitaire »). Il ne change plus ensuite : une réception ou une correction ultérieure ne modifie pas les ventes passées.
Un produit absent du catalogue n'a pas de coût. Les ventes enregistrées avant cette évolution n'ont pas de coût retenu.

### CM-03 Marge exacte
La marge d'un produit sur une période est `chiffre d'affaires − Σ(quantité × coût unitaire retenu)`. Pour les lignes **sans coût retenu** (ventes antérieures), on prend le prix d'achat actuel du produit et la marge est signalée **estimée**.
Le classement par marge, et la marge affichée, disent clairement « estimée » dès qu'une seule ligne de la période est estimée ; sinon elle est exacte. La marge reste réservée aux profils qui voient les prix d'achat (propriétaire, gérant).

### CM-04 Traçabilité
Un mouvement de réception garde le prix d'achat unitaire de sa livraison. L'historique du produit l'affiche (« à 450 FCFA l'unité ») pour les profils qui voient les prix d'achat.

### CM-05 Écran
- Réception : le champ devient « Prix d'achat de cette livraison (FCFA, facultatif) » avec, dessous, « Prix d'achat moyen actuel : X. Il sera recalculé avec cette livraison. » Après l'enregistrement : « Réception enregistrée. Prix d'achat moyen : Y. »
- Fiche produit : « Prix d'achat moyen (FCFA) », avec l'aide « Recalculé à chaque réception où vous indiquez le prix de la livraison. »
- Meilleures ventes : l'aide sous le critère « Marge » explique le calcul en mots simples ; une marge estimée est marquée « (estimée) ».

## Scénarios d'acceptation

- **SC-M1** Stock 10 à 400 FCFA, réception de 10 à 500 → prix moyen 450 ; stock 20.
- **SC-M2** Stock 3 à 400, réception de 1 à 500 → 425 ; arrondi au plus proche : stock 1 à 400 + 2 à 401 → 400,67 → 401.
- **SC-M3** Stock 0 ou négatif (−4) puis réception de 10 à 500 → le prix moyen devient 500 (le stock négatif ne pèse pas) ; stock 6.
- **SC-M4** Réception sans prix de livraison : prix moyen inchangé, mouvement sans prix.
- **SC-M5** Une vente après la réception retient le prix moyen du moment ; une nouvelle réception change le prix moyen mais pas cette vente.
- **SC-M6** Le classement par marge utilise les coûts retenus : deux ventes de périodes de coûts différents donnent la marge exacte, sans mention « estimée ».
- **SC-M7** Des ventes sans coût retenu donnent une marge estimée avec le prix actuel, signalée comme telle ; mélange de lignes avec et sans coût : estimée.
- **SC-M8** Un prix de livraison négatif ou une quantité nulle est refusé ; un comptable ou un caissier ne voit ni prix moyen, ni coût, ni marge.
- **SC-M9** Rejouer une réception (même identifiant de mouvement) ne recalcule pas le prix moyen une seconde fois.

## Hors lot

Valorisation du stock, rapports de marge globale, coût par lot/péremption (FIFO), annulation d'une vente remettant le coût, import de factures fournisseur, prix d'achat par fournisseur.
