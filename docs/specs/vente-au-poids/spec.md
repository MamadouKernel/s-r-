# Spécification — Vente au poids et à la mesure

**Statut :** brouillon à valider ; décisions de `docs/decisions/0017-vente-au-poids-et-a-la-mesure.md`.
**Lots :** (1) domaine, serveur et migration ; (2) caisse (saisie du poids ou du montant, panier, ticket) ; (3) gestion (fiche produit, réception, perte, import et export).

## Objectif

Vendre un produit **au kilo ou au litre** avec la quantité que demande le client (« 1,5 kg », « 2 litres », « pour 1 000 francs »), sans créer un produit par quantité, avec un stock, un coût moyen et des totaux **exacts**.

## Exigences

### VPO-01 L'unité d'un produit
`Pièce` (défaut), `Kilogramme`, `Litre`, portée par chaque produit (format) à sa création. Elle ne change plus dès que le produit a du stock non nul ou une vente ou un mouvement ; le serveur refuse avec « Cette unité ne peut plus changer : ce produit a déjà un historique. » (422).

### VPO-02 Les quantités
Stockées en entiers : pièces telles quelles ; kilogrammes en grammes ; litres en millilitres (×1 000). Ce choix est appliqué à la **vente**, au **stock** (quantité en main, seuil d'alerte, mouvements) et au **coût moyen**. Aucune colonne ne change de type ; aucun calcul en virgule flottante.

### VPO-03 Le prix et le total d'une ligne
Prix = prix d'**une** unité (kg ou litre). Total de ligne = `arrondi(prix × quantité ÷ 1 000)` pour un produit pesé, `prix × quantité` pour une pièce ; arrondi **au franc le plus proche, demi vers le haut**. Total de la vente = somme des totaux de ligne arrondis. Le serveur recalcule et **refuse** une vente dont les totaux annoncés diffèrent (même contrôle qu'aujourd'hui).

### VPO-04 Le stock suit l'unité
Une vente retire sa quantité exacte en grammes ou millilitres ; une réception ajoute dans l'unité du produit (prix d'achat **à l'unité**) ; une perte accepte des quantités décimales ; l'annulation d'une vente remet la quantité exacte. Le coût moyen pondéré (RG-11) est calculé par unité, arrondi au franc par unité comme aujourd'hui. Le seuil d'alerte est exprimé dans l'unité du produit.

### VPO-05 La ligne garde son unité
La ligne de vente enregistre l'**unité** du moment (copie figée, comme le nom et le coût). L'historique, le ticket et l'export l'utilisent.

### VPO-06 La caisse : poids ou montant
Un produit pesé s'ajoute au panier par un petit écran :
- **Poids** (« Poids en kg » / « Litres ») : virgule ou point, trois décimales au plus ; « 1,5 » = 1,500 kg.
- **Montant** (« Montant en FCFA ») : calcule la quantité = `montant × 1 000 ÷ prix` **arrondie au gramme** (ou millilitre), puis le **total réel** recalculé (le montant demandé peut différer de quelques francs à cause de l'arrondi : l'écran le dit, « 1 000 FCFA → 1,250 kg »).
- Le résultat (« 1,250 kg · 1 000 FCFA ») est montré **avant** l'ajout ; une quantité nulle, négative ou plus grande que le stock suivi est refusée avec une phrase.
- Ajouter deux fois le même produit **additionne** les quantités ; la ligne du panier se corrige en retapant le poids.
- Le scan d'un code-barres d'un produit pesé ouvre le même écran (le poids reste à saisir).

### VPO-07 Affichage
Quantités écrites avec la virgule française et l'unité : « 1,250 kg », « 0,5 L », « 3 » pour des pièces. Le prix d'un produit pesé s'écrit « 800 FCFA / kg ». Le stock s'écrit « 25,500 kg ». Une seule fonction de mise en forme sert partout (écran, ticket, export, journal).

### VPO-08 Gestion
- **Fiche produit / format** : choix de l'unité à la création (lecture seule ensuite, avec la raison), prix de vente et d'achat « par kg / par litre », seuil d'alerte dans l'unité.
- **Réception** et **perte** : quantité décimale dans l'unité.
- **Import Excel** : colonne facultative « Unité » (« pièce », « kg », « litre » ; vide = pièce) ; les quantités et prix sont lus dans cette unité.
- **Export** : colonne « Unité » ; quantités en unités lisibles (1,250), jamais en grammes.

### VPO-09 Compatibilité
Les produits existants sont des pièces (migration par valeur par défaut). Les ventes déjà en file d'attente sur un appareil sont lues comme des pièces ; un appareil qui n'a pas encore l'application à jour **ne peut pas vendre** un produit pesé : le serveur refuse sa ligne avec un message demandant de mettre à jour l'application (jamais une quantité mal interprétée).

### VPO-10 Hors ligne
La saisie, le calcul, le total et le ticket fonctionnent hors ligne avec la copie locale du catalogue (qui porte l'unité).

## Scénarios d'acceptation

1. **Riz au kilo.** Riz à 800 FCFA/kg, 25 kg en stock : vendre 1,250 kg donne 1 000 FCFA, stock 23,750 kg, ticket « 1,250 kg × 800 FCFA ».
2. **Pour 1 000 francs.** Saisir 1 000 FCFA de riz à 800 FCFA/kg : 1,250 kg, total 1 000 FCFA.
3. **Montant qui ne tombe pas juste.** 1 000 FCFA d'un produit à 700 FCFA/kg : 1,429 kg (arrondi au gramme), total réel 1 000 FCFA (1,429 × 700 = 1 000,3 → 1 000) ; l'écran écrit le résultat avant l'ajout.
4. **Arrondi.** 0,333 kg à 800 FCFA/kg = 266,4 → 266 FCFA ; 0,3335 n'existe pas (trois décimales au plus) ; demi vers le haut : 0,0625… hors trois décimales ; 0,1875 kg refusé (4 décimales).
5. **Total exact.** Trois lignes pesées : le total encaissé est la somme des totaux de ligne arrondis, égal à celui du ticket et de l'historique.
6. **Pièce inchangée.** Une bouteille à 600 FCFA vendue ×2 donne 1 200 FCFA, comme avant ; aucune virgule affichée.
7. **Stock insuffisant.** Vendre 30 kg quand il en reste 25,500 : refusé avec « Il n'en reste que 25,500 kg. » (produit suivi).
8. **Réception au kilo.** 25 kg à 15 000 FCFA reçus puis 10 kg à 16 500 : coût moyen par kg pondéré exact, arrondi au franc par kg.
9. **Perte décimale.** Perdre 0,5 kg retire 500 g du stock.
10. **Annulation.** Annuler la vente du scénario 1 remet exactement 1,250 kg.
11. **Unité figée.** Changer l'unité d'un produit qui a une vente est refusé ; l'historique d'une vente passée garde « kg ».
12. **Ancien appareil.** Une ligne pesée envoyée sans unité est refusée avec le message de mise à jour ; rien n'est enregistré.
13. **Import.** Une ligne « Sucre ; kg ; 700 ; … » crée un produit au kilo ; sans unité, une pièce.
14. **Export.** Les ventes pesées s'écrivent « 1,250 » avec la colonne « Unité » = « kg ».

## Hors lot

Balance connectée, codes EAN « prix/poids » des balances de supermarché, conditionnement multiple (acheter au sac, vendre au kilo avec reste automatique), taxes, lecture de la balance par Bluetooth.
