# Spécification — Import des produits depuis Excel

**Statut :** brouillon ; décisions prises avec le porteur du projet le 25/10/2026 (voir `docs/decisions/0010-import-excel.md`). S'appuie sur `docs/specs/formats-produits/spec.md` (familles et formats), `docs/specs/catalogue-stock/spec.md` et `docs/decisions/0007-cout-moyen-pondere.md`.
**Lots :** (1) serveur : modèle, vérification, import ; (2) écran : « Importer depuis Excel », résumé, liste des lignes à corriger.

## Objectif

Un commerçant qui démarre a souvent déjà sa liste de produits dans Excel. Il doit pouvoir la charger d'un coup : produits, formats, catégories, prix, stock de départ. Le contrôle se fait **avant** d'enregistrer, les erreurs sont dites en français simple avec le **numéro de ligne**, et rien n'est créé tant que tout n'est pas juste.

## Principe

Une ligne du fichier = **un format** (un produit vendable). Plusieurs lignes qui portent le **même nom de produit** forment une **famille** (ex. trois lignes « Riz parfumé » : 5 kg, 25 kg, 50 kg). Une ligne sans format est un produit seul. Le fichier ne contient ni photo ni client ; il ne **modifie jamais** un produit existant.

## Exigences

### IMP-01 Modèle à télécharger
`GET /api/v1/product-imports/template` renvoie un classeur `.xlsx` : une feuille **« Produits »** avec l'en-tête et deux lignes d'exemple, une feuille **« Mode d'emploi »** qui explique chaque colonne en français. Lecture : propriétaire et gérant.
Colonnes, dans cet ordre : **Nom du produit** · **Format** · **Catégorie** · **Code-barres** · **Prix d'achat** · **Prix de vente** · **Alerte stock** · **Stock de départ** · **Suivi du stock**.

### IMP-02 Fichier accepté
Un vrai classeur `.xlsx`, reconnu par son **contenu** (pas par son nom), de **2 Mo au plus** et **2 000 lignes de produits au plus**. La feuille lue est « Produits », à défaut la première. L'en-tête est retrouvé par le **nom des colonnes** (sans tenir compte des accents, de la casse ni de l'ordre), pas par leur position ; une colonne obligatoire absente est signalée. Les lignes entièrement vides sont ignorées. Un fichier protégé par mot de passe, abîmé ou d'un autre format (CSV renommé, ancien `.xls`) est refusé avec un message clair.

### IMP-03 Règles de chaque ligne
- **Nom du produit** : obligatoire, 200 caractères au plus. Les lignes de même nom (accents, casse et espaces ignorés) forment une famille.
- **Format** : 40 caractères au plus ; **obligatoire dès qu'une famille a plusieurs lignes** ; unique dans la famille (sans tenir compte de la casse ni des espaces).
- **Catégorie** : 100 caractères au plus. Les lignes d'une même famille doivent donner la **même** catégorie (ou la laisser vide) ; deux catégories différentes sont signalées.
- **Code-barres** : facultatif, 64 caractères au plus, **unique** dans le fichier et dans le commerce. Excel transforme parfois un code en nombre (`6.111E+12`, zéros perdus) : une valeur ainsi abîmée est signalée plutôt que devinée.
- **Prix d'achat** : entier en FCFA, 0 si vide. **Prix de vente** : entier en FCFA, **obligatoire**. Sont acceptés : `1500`, `1 500`, `1500 FCFA`, `1500,0` ; un prix avec des centimes (`12,5`) ou négatif est refusé.
- **Alerte stock** : entier ≥ 0, 0 si vide.
- **Stock de départ** : entier ≥ 0, 0 si vide ; impossible quand le suivi du stock est « Non ».
- **Suivi du stock** : « Oui » ou « Non » (ou 1/0, vrai/faux), « Oui » si vide.

### IMP-04 Produits déjà dans le catalogue
- Une ligne dont le **code-barres** existe déjà, ou dont le **nom et le format** existent déjà, est **ignorée** et listée comme « déjà dans votre catalogue » : ce n'est **pas une erreur**, pour qu'on puisse réimporter le même fichier sans doublon.
- Une ligne dont le **nom de famille existe déjà** mais avec un **nouveau format** est **ajoutée à cette famille**, à condition que la famille ait déjà des formats nommés. Si la famille existante n'a qu'un produit sans format, la ligne est une **erreur** qui dit d'ajouter le format depuis la fiche du produit (sinon il faudrait renommer un produit existant).
- Aucun produit existant n'est jamais modifié (prix, stock, catégorie restent ce qu'ils sont).

### IMP-05 Vérification sans enregistrer
`POST /api/v1/product-imports/preview` (le fichier en corps de requête) renvoie le **compte rendu** sans rien écrire :
- nombre de lignes lues, de **produits** (familles) et de **formats** qui seraient créés, de lignes **déjà dans le catalogue** ;
- la liste des **problèmes** : numéro de ligne, colonne, message en français (« Ligne 12 : le prix de vente doit être un nombre entier en FCFA. ») ; **50 au plus**, avec le total ;
- `canImport` : vrai s'il n'y a aucun problème et au moins une ligne à créer.
Aucun jargon, aucun identifiant technique dans les messages.

### IMP-06 Importation, tout ou rien
`POST /api/v1/product-imports?importId=…` (même corps) refait la vérification puis crée **tout dans une seule transaction** : si un seul problème existe, **rien** n'est créé et le compte rendu des problèmes est renvoyé (422). Sinon : familles, formats, et pour chaque **stock de départ > 0** un mouvement de réception « Stock de départ » (fournisseur : « Import Excel »), avec le prix d'achat de la ligne comme coût (le coût moyen part de ce prix, RG-11). Réponse : ce qui a été créé.
**Rejouable** : les identifiants des familles et des produits sont déduits de `importId`, du nom et du format ; rejouer le même import ne crée rien de plus (réponse « déjà traité »).
Journal des actions sensibles : `products.imported` (qui, quand, combien de produits et de formats).

### IMP-07 Droits, taille et isolation
Propriétaire et gérant seulement (403 sinon, magasinier compris). Un fichier trop lourd ou trop long est refusé avant toute lecture complète (413 / 422 avec message). L'import ne touche que le commerce de l'utilisateur ; un code-barres d'un autre commerce n'est pas un conflit.

### IMP-08 Sûreté du fichier
Le fichier est **traité comme une donnée non fiable** : lecture bornée en taille et en lignes, aucune formule évaluée (seules les valeurs sont lues), aucun lien externe suivi, aucun accès disque. Un texte qui commence par `=`, `+`, `-` ou `@` est gardé tel quel comme texte.

### IMP-09 Écran (lot 2)
Dans **Gestion → Produits** : « Importer depuis Excel » (propriétaire et gérant). L'écran propose « Télécharger le modèle », puis le choix du fichier ; il affiche aussitôt le résumé de la vérification et, s'il y a des problèmes, le tableau « Ligne · Colonne · À corriger » ; « Importer ces N produits » n'est actif que si tout est juste ; après l'import, un message dit ce qui a été créé et renvoie à la liste. En ligne seulement.

## Scénarios d'acceptation

1. **Fichier juste.** 3 lignes « Riz parfumé » (5 / 25 / 50 kg) et 1 ligne « Sel fin » : la vérification annonce 2 produits et 4 formats ; l'import les crée avec leurs prix, catégories et seuils.
2. **Stock de départ.** « 25 kg » avec stock 6 et prix d'achat 11 500 : le stock est 6, le coût moyen 11 500, l'historique montre « Stock de départ ».
3. **Une erreur.** La ligne 5 a un prix de vente « abc » : message « Ligne 5 : … », rien n'est créé même pour les lignes justes.
4. **Plusieurs erreurs.** Elles sont toutes listées en une fois (jusqu'à 50), pour tout corriger d'un coup.
5. **Format manquant.** Deux lignes « Eau » dont une sans format : erreur sur la ligne sans format. Un doublon de format (« 1 L » et « 1 l ») est signalé.
6. **Code-barres.** Doublon dans le fichier, ou déjà utilisé dans le commerce : signalé ; code abîmé par Excel (`6.111E+12`) : signalé.
7. **Réimport.** Le même fichier importé deux fois : la seconde fois, tout est « déjà dans votre catalogue » et rien n'est créé ni modifié.
8. **Nouvelle taille.** « Riz parfumé · 1 kg » dans un fichier alors que « Riz parfumé » a déjà 5/25/50 kg : le format est ajouté à la famille.
9. **Famille sans format.** « Sel fin » existe seul ; une ligne « Sel fin · 500 g » : erreur qui renvoie à la fiche du produit.
10. **Suivi du stock « Non ».** Une prestation (« Coupe ») sans stock ; stock de départ renseigné : erreur.
11. **Mauvais fichier.** Un CSV renommé `.xlsx`, un `.xls`, un fichier de 5 Mo, 3 000 lignes : refusés avec un message clair, sans rien lire de plus.
12. **Formule.** Une cellule `=1+1` en nom est gardée comme texte, jamais calculée.
13. **Droits.** Le caissier, le magasinier, le comptable : 403 sur le modèle, la vérification et l'import.
14. **Rejeu.** Même `importId` rejoué après une coupure : rien de créé en double.
15. **Isolation.** Le commerce B n'est pas gêné par les codes-barres du commerce A.

## Hors lot

- Mise à jour de produits existants par fichier (prix, stock, catégorie) ; suppression par fichier.
- Photos, clients, fournisseurs ; export du catalogue vers Excel ; CSV et autres formats.
- Correspondance de colonnes personnalisée (le modèle fait foi).
