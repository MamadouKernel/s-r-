# 0010 — Import des produits depuis Excel

**Statut :** accepté le 25/10/2026 (recommandations du porteur du projet suivies).

## Contexte

Saisir un catalogue produit par produit est long pour un commerce qui a déjà sa liste dans Excel. Les familles et formats (0009) rendent le fichier plus riche : plusieurs lignes peuvent former une famille.

## Décisions

1. **Une ligne = un format ; même nom = même famille.** Pas de feuille « familles » séparée : un fichier plat, simple à remplir.
2. **Tout ou rien.** Une seule ligne fausse : rien n'est créé, et toutes les erreurs sont listées en une fois. Un import partiel créerait des doublons dès qu'on réimporte le fichier corrigé.
3. **Jamais d'écrasement.** Un produit déjà présent (code-barres, ou nom et format) est ignoré et listé ; on peut réimporter sans risque. La mise à jour par fichier est un autre lot.
4. **Un nouveau format s'ajoute à une famille existante** quand celle-ci a déjà des formats nommés ; sinon l'écran renvoie à la fiche (renommer un produit existant en silence serait pire qu'une erreur).
5. **Stock de départ = une réception** « Stock de départ » dans l'historique, avec le prix d'achat de la ligne comme coût (le coût moyen part de là, 0007). Pas de stock « magique » sans mouvement.
6. **Lecture sur le serveur**, avec ClosedXML (MIT) : une seule règle de vérification, la même que la saisie ; le fichier est une donnée non fiable (taille, lignes, aucune formule évaluée). Le modèle est lui aussi produit par le serveur.
7. **Vérification et import séparés** (`preview` puis import) mais le **même code** : l'import refait la vérification, il ne fait pas confiance à la précédente.
8. **Rejouable** : les identifiants sont déduits de `importId`, du nom et du format ; rejouer ne crée rien de plus.
9. **Droits** : propriétaire et gérant, comme la création de produits.

## Risques et limites

- Excel abîme parfois les codes-barres (notation scientifique, zéros perdus) : on le signale au lieu de deviner. Le modèle met la colonne au format texte.
- 2 000 lignes et 2 Mo : assez pour une supérette ; au-delà, on découpe le fichier.
- Le comptage « produits / formats » du résumé suppose des noms écrits à l'identique (accents et casse ignorés) ; « Riz parfumé » et « Riz parfume » sont la même famille, « Riz » et « Riz long » non.
