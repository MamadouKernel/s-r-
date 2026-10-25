# 0009 — Familles de produits et formats, photo facultative

**Statut :** proposé le 25/10/2026, à valider avec le porteur du projet (le principe « un format = un article vendable » et l'ajout d'une photo facultative sont déjà demandés).

## Contexte

Une boutique vend la même marchandise en plusieurs formats (eau 1,5 L / 1 L / 30 cl, riz 5 / 25 / 50 kg), chacun avec son code-barres, son prix, son prix d'achat et son stock. Aujourd'hui Sôrô n'a que des produits isolés : il faudrait écrire « Eau 1,5 L » à la main, sans lien entre les formats.

## Décisions

1. **Un format est un produit vendable complet ; une famille les regroupe.** Pas de « produit à variantes » abstrait : le stock, le coût moyen (0007), la marge, la synchronisation hors ligne et le journal restent par format et ne changent pas. Un produit actuel devient une famille à un format. C'est aussi ce que font les vrais commerces : le code-barres désigne un format, jamais une famille.
2. **Le nom affiché est calculé par le serveur** (« Famille · libellé ») et vaut partout. Les ventes passées gardent l'instantané du nom, qui existe déjà.
3. **Libellé libre, unique dans la famille** (sans casse ni espaces superflus), obligatoire dès deux formats. Pas de grille d'attributs : un seul champ que le commerçant comprend (« 1,5 L », « 50 kg », « Pack de 6 »).
4. **Photo facultative, sur la famille, avec remplacement possible par format.** Elle ne fait pas partie du catalogue synchronisé : le catalogue ne porte que son identifiant, l'image se charge à part et se met en cache sur l'appareil. L'application réduit l'image avant l'envoi (480 px, JPEG, ~60 Ko) ; le serveur plafonne à 200 Ko et reconnaît le type par le contenu. Stockage en base, dans une table à part avec isolation par commerce, tant que le volume reste petit ; un stockage de fichiers pourra le remplacer sans changer l'interface. Sans photo, ou hors ligne sans cache : les initiales.
5. **Reprise sans perte** : chaque produit existant devient sa propre famille. Un outil « Regrouper dans une famille » permet ensuite de rassembler des produits que le commerçant avait saisis séparément.
6. **Choix du format à la caisse après recherche par nom** (un gros bouton par format), **scan direct** sur le format. La recherche par nom reste utilisable hors ligne.
7. **Cartons, sacs ouverts et vente au poids sont deux lots à part** : ils changent les quantités (conversions, décimales) et le calcul du coût, et se spécifient séparément pour ne pas mêler deux risques.

## Risques et limites

- Le regroupement d'anciens produits change leur nom affiché : le gérant doit le faire en connaissance de cause (confirmation à l'écran).
- Une photo trop lourde est refusée côté serveur si l'application ne la réduit pas assez (navigateur très ancien) : message clair, jamais bloquant pour la vente.
- Le stock n'est jamais partagé entre formats : vendre un 1 L ne retire rien au 1,5 L. Le partage (sac ouvert) relève du lot 3.
