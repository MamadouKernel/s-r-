# Spécification : Catalogue et stock (V1)

**Statut :** brouillon dérivé du cahier des charges (sections 5 et 6) ; à valider avec le commerce pilote.

## Objectif

Donner au commerce un catalogue de produits (prix, code-barres, seuil d'alerte) et un stock qui baisse à chaque vente,
où chaque entrée et sortie est attribuée à une personne, une date et un motif.

## Exigences

### CAT-01 Fiche produit
Un gérant ou le propriétaire crée un produit : nom, code-barres (optionnel), catégorie (optionnelle), prix d'achat,
prix de vente, seuil d'alerte, suivi du stock activé ou non (prestation sans stock). Le code-barres est unique dans le
commerce. Création rejouable par identifiant de produit (hors ligne, RG-17).

### CAT-02 Modification et désactivation
Un gérant modifie nom, prix, code-barres, catégorie et seuil. Un produit ne se supprime jamais : il se désactive
(il reste lisible dans l'historique des ventes). Un produit désactivé n'apparaît plus dans la liste de vente.

### CAT-03 Consultation
Tous les profils authentifiés lisent le catalogue de leur commerce, avec recherche par nom ou code-barres.
Le prix d'achat n'est visible que du propriétaire, du gérant et du magasinier.

### STK-01 Réception fournisseur
Le propriétaire, le gérant ou le magasinier enregistre une entrée de marchandise : quantité strictement positive,
fournisseur ou référence en texte libre (optionnel), prix d'achat (optionnel, met à jour la fiche).

### STK-02 Pertes et casse
Sortie hors vente (périmé, cassé, consommation interne) : quantité positive, motif obligatoire. Même droits que STK-01.

### STK-03 Mouvements append-only
Chaque variation de stock est un mouvement signé, jamais modifié ni supprimé : réception, vente, perte, annulation de vente.
La quantité en stock d'un produit est la somme de ses mouvements. Une correction est un mouvement inverse motivé.

### STK-04 Décrément à la vente
Chaque vente validée d'un produit suivi crée un mouvement `Sale` par ligne. Le stock peut devenir négatif : la vente n'est
jamais bloquée (cas hors ligne, question OP-4 ouverte). Les produits inconnus du catalogue ou non suivis sont ignorés.

### STK-05 Alerte de rupture
Liste des produits suivis dont le stock est inférieur ou égal au seuil d'alerte (et seuil > 0), triée par urgence.

## Scénarios d'acceptation

- **SC-C1** Un caissier tente de créer un produit : refusé (403).
- **SC-C2** Deux produits avec le même code-barres dans un commerce : le second est refusé (409) ; dans deux commerces différents : accepté.
- **SC-C3** Réception de 24 unités puis vente de 3 : le stock vaut 21 et l'historique contient deux mouvements attribués.
- **SC-C4** Perte sans motif : refusée.
- **SC-C5** Un produit passé sous son seuil apparaît dans les alertes.
- **SC-C6** Un utilisateur d'un autre commerce ne voit ni ne modifie ce produit.
- **SC-C7** Rejouer la création ou la réception avec le même identifiant ne crée rien de plus.

## Questions ouvertes

- Stock négatif : bloquer, avertir ou accepter après resynchronisation (OP-4) ? Retenu provisoirement : accepter.
- Retour du stock lors d'une annulation de vente (T017) : le mouvement `SaleCancellation` est prévu mais non branché.
- Photo du produit, import/export Excel, catalogue prérempli : hors de ce lot.
- Inventaire (comptage) : hors de ce lot.
