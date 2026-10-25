# 0017 — Vente au poids et à la mesure

**Statut :** proposé le 25/10/2026, à valider avec le porteur du projet (qui a délégué le choix « selon l'expérience »). Complète `docs/decisions/0009` (familles et formats) et `0007` (coût moyen).

## Contexte

Dans la vie réelle d'un commerce ivoirien, une grande part du chiffre se fait **au poids ou à la mesure** : riz, sucre, haricots, farine, huile, tomates, poisson, charbon. Le client demande « 1,5 kg » ou « 2 litres » ou « pour 1 000 francs de riz ». Aujourd'hui toute quantité est un **nombre entier** : on ne peut vendre qu'à l'unité, et on contourne en créant un produit « riz 1 kg », « riz 500 g », « riz 250 g », ce qui fausse le stock (trois stocks pour un même sac) et le coût moyen.

## Décisions

1. **Une unité par produit** : `Pièce` (par défaut, comme aujourd'hui), `Kilogramme` ou `Litre`. Elle se choisit à la création du produit ou du format ; elle ne change plus tant que le produit a du stock ou des ventes (changer l'unité falsifierait l'historique).
2. **Les quantités restent des entiers** : pour un produit au `Kilogramme` ou au `Litre`, la quantité est stockée en **millièmes** (grammes, millilitres) ; pour une `Pièce`, elle ne change pas. **Aucun type de colonne ne change** (entiers 32 bits : jusqu'à plus de 2 000 tonnes ou 2 millions de litres par produit), aucun calcul en virgule flottante : l'argent et les stocks restent exacts.
3. **Le prix est le prix d'une unité** (« 800 FCFA le kg »). Le total d'une ligne est le prix × la quantité, **arrondi au franc le plus proche** (demi vers le haut), jamais en décimales (RG-18). Le total de la vente est la somme des lignes arrondies : ce que le client voit sur le ticket est exactement ce qui est encaissé.
4. **La caisse saisit une quantité décimale ou un montant** : pour un produit pesé, un pavé propose « Poids (kg) » ou « Litres » (virgule ou point acceptés, trois décimales au plus) **ou « Montant (FCFA) »** : « pour 1 000 francs de riz » calcule le poids (1 000 ÷ 800 = 1,250 kg). Le résultat est montré avant l'ajout, et arrondi au gramme.
5. **Le coût moyen, le stock et les pertes suivent l'unité** : la réception « 25 kg à 15 000 FCFA le sac » se saisit dans l'unité du produit (25 kg, prix d'achat au kg) ; le coût moyen pondéré (RG-11) se calcule par unité ; une perte de « 0,5 kg » est possible.
6. **La ligne de vente garde l'unité** (copie figée au moment de la vente) : le ticket, l'historique et l'export écrivent « 1,250 kg × 800 FCFA = 1 000 FCFA », même si le produit change plus tard.
7. **Aucune balance connectée dans ce lot** : le caissier lit la balance et tape le poids. La lecture automatique d'une balance (USB/Bluetooth) est un chantier ultérieur ; le lecteur de code-barres reste inchangé.
8. **Compatibilité** : tous les produits existants sont des `Pièce` (migration sans perte, valeur par défaut) ; l'application hors ligne déjà installée continue de vendre des pièces ; les ventes en attente d'envoi sont lues comme avant.
9. **Hors lot** : balance connectée, étiquettes à poids imprimé (codes EAN « prix/poids » des balances de supermarché), conditionnement multiple (sac 25 kg → vendu au kg avec reste automatique), taxes.

## Risques et limites

- Changer le nombre de décimales après coup n'est pas prévu (trois décimales : gramme ou millilitre).
- Le stock d'un produit au poids se lit en kg à l'écran mais s'enregistre en grammes : toute mise en forme du stock doit passer par la même fonction (test de cohérence).
- Un produit au détail issu d'un sac (riz) reste un produit séparé du sac entier tant que le conditionnement multiple n'existe pas ; c'est le cas courant (on achète « au sac », on vend « au kilo ») et il fera l'objet de la décision suivante.
