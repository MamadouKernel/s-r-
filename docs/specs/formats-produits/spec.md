# Spécification — Familles de produits et formats (lot 1), photo facultative

**Statut :** brouillon à valider ; décisions prises avec le porteur du projet le 25/10/2026 (voir `docs/decisions/0009-familles-et-formats.md`). S'appuie sur `docs/specs/catalogue-stock/spec.md` (serveur) et `docs/specs/gestion-produits-stock/spec.md` (écrans de gestion).
**Lots :** (1) **ce document** : familles, formats, photo facultative, choix du format à la caisse ; (2) achats par carton ou sac (conversion en unités, coût unitaire) ; (3) vente au poids et sac ouvert. Les lots 2 et 3 ont chacun leur spécification.

## Objectif

Dans un commerce réel, une « bouteille d'eau » ou un « sac de riz » existe en plusieurs **formats** (1,5 L, 1 L, 30 cl ; 5 kg, 25 kg, 50 kg). Chaque format est un article distinct : son code-barres, son prix de vente, son prix d'achat, son stock et son seuil d'alerte lui sont propres. Ce qui est commun : le nom (« Eau minérale »), la catégorie et, si on veut, une photo.
Sôrô doit permettre de saisir un produit **avec ses formats**, de les retrouver à la caisse (scan, ou recherche puis choix du format en un geste) et de ne jamais mélanger les stocks ni les prix.

## Principe

Un **format est un produit vendable complet**, exactement comme un produit aujourd'hui. Une **famille** regroupe un ou plusieurs formats. Stock, coût moyen, marge, ventes, synchronisation hors ligne et journal restent **par format** et ne changent pas. Un produit actuel devient une famille à un seul format.

## Exigences

### FMT-01 Famille et formats
Une famille a un **nom** (1 à 200 caractères, comme un produit aujourd'hui), une **catégorie** facultative et au moins un format actif tant qu'elle est proposée à la vente. Un format a : un **libellé** (1 à 40 caractères : « 1,5 L », « 50 kg », « Pack de 6 »), un **code-barres** facultatif, un prix d'achat, un prix de vente, un suivi du stock (choisi à la création, non modifiable), un seuil d'alerte, un stock. Règles de valeur : celles de GES-02 (FCFA entiers, prix de vente ≥ prix d'achat non exigé).
- Le libellé est **unique dans la famille** (sans tenir compte des majuscules ni des espaces en trop).
- Une famille à **un seul format** peut ne pas avoir de libellé (« Sel fin », « Savon Omo »). Dès qu'elle a deux formats, **tous** ont un libellé.
- Le code-barres reste unique dans le commerce (409 sinon, comme aujourd'hui).

### FMT-02 Nom affiché
Le nom affiché d'un format est « **Nom de la famille · libellé** » (« Eau minérale · 1,5 L ») ou le nom seul sans libellé. Il est calculé par le serveur et c'est lui qui apparaît partout : catalogue de la caisse, panier, ticket, journal, mouvements de stock, meilleures ventes. Renommer une famille ou un libellé met à jour les noms affichés ; **les ventes passées gardent le nom qu'elles avaient** (instantané déjà en place).

### FMT-03 Créer et modifier
- Créer une famille avec ses premiers formats en **une seule opération** (tout ou rien, rejouable par identifiant) ; ajouter un format plus tard ; modifier le nom et la catégorie de la famille ; modifier le libellé, les prix, le code-barres et le seuil d'un format.
- **Désactiver un format** (il reste dans l'historique, n'est plus vendable). Désactiver le dernier format actif retire la famille de la caisse. Un format dont le stock n'est pas à zéro peut être désactivé après confirmation explicite (comme aujourd'hui pour un produit).
- Droits : ceux de GES (propriétaire et gérant ; le magasinier ne modifie aucune fiche).

### FMT-04 Photo facultative
- Une famille peut avoir **une photo**, jamais obligatoire. Un format peut avoir **sa propre photo** qui remplace celle de la famille (utile quand l'emballage diffère) ; sans photo propre, il montre celle de la famille.
- Ajout depuis l'appareil photo du téléphone ou un fichier ; **l'application réduit l'image** avant l'envoi (côté le plus long 480 px, JPEG ; viser moins de 60 Ko) pour que ça marche avec une connexion faible.
- Le serveur n'accepte que JPEG, PNG ou WebP **reconnus par leur contenu** (pas par le nom du fichier), 200 Ko au plus ; sinon message clair (« Cette image est trop lourde » / « Ce fichier n'est pas une image »). Remplacer ou retirer une photo est possible à tout moment ; l'image retirée est vidée de la base (le rôle applicatif ne supprime jamais de ligne).
- Une photo est **privée au commerce** (isolation comme le reste) ; elle n'est servie qu'à un utilisateur connecté de ce commerce.
- **Sans photo, rien ne manque** : l'écran affiche les initiales du produit. Hors ligne, une photo déjà vue reste affichée ; une photo jamais chargée est remplacée par les initiales, sans erreur.
- Les montants, le stock et la vente ne dépendent jamais d'une photo.

### FMT-05 Catalogue envoyé à la caisse
Chaque article du catalogue de la caisse indique : identifiant du format, identifiant de la famille, nom de la famille, libellé, **nom affiché**, code-barres, catégorie, prix de vente, stock, et l'identifiant de la photo à utiliser (la sienne, sinon celle de la famille, sinon rien). **Le prix d'achat n'y figure toujours pas.** Les photos ne sont pas dans le catalogue : elles se chargent à part, à la demande.

### FMT-06 Caisse
- **Scan** : le code-barres désigne un format, qui entre directement dans le panier.
- **Recherche par nom** : les résultats sont **regroupés par famille**. Une famille à un seul format s'ajoute d'un toucher. Une famille à plusieurs formats ouvre le **choix du format** : un gros bouton par format (48 px au moins) avec libellé, prix et disponibilité en toutes lettres (« 12 en stock », « Plus que 3 », « Épuisé »). Un format épuisé reste visible et indique la règle de stock du commerce (comme aujourd'hui).
- La recherche trouve aussi par libellé : « riz 25 » trouve « Riz parfumé · 25 kg ».
- Le panier et le ticket montrent le **nom affiché** ; deux formats de la même famille sont deux lignes distinctes.
- Tout cela fonctionne **hors ligne** à partir de la copie locale du catalogue.

### FMT-07 Gestion (écrans)
- La liste des produits montre les **familles**, chacune dépliable en formats avec prix, stock, seuil et statut en toutes lettres ; la recherche trouve par nom, libellé ou code-barres exact ; « Stock bas » liste les **formats** concernés.
- La fiche s'ouvre sur la famille (nom, catégorie, photo) puis la **liste des formats** avec « Ajouter un format » ; chaque ligne se modifie ou se désactive. Un langage simple : « format », « photo », « prix d'achat » ; aucun identifiant technique.
- Réception, perte et historique de stock se font **par format** (le nom affiché dit lequel).
- Écrans en ligne seulement, comme GES.

### FMT-08 Reprise de l'existant
À la mise en service, chaque produit actuel devient **sa propre famille à un format sans libellé** : même nom, même stock, même prix, mêmes mouvements, mêmes ventes. Aucun montant ni aucun stock ne change ; les ventes déjà enregistrées ne sont pas modifiées. La famille d'origine d'un produit regroupé reste en base, vide et invisible (aucune suppression de ligne côté application). Pour regrouper des produits existants en une famille (par exemple « Eau 1,5 L » et « Eau 50 cl »), le gérant utilise **« Regrouper dans une famille »** : il choisit des produits, donne le nom de la famille et le libellé de chacun. Le nom affiché peut alors changer, pas les ventes passées.

### FMT-09 Copie locale de la caisse
La copie locale du catalogue gagne les champs de FMT-05. Une copie ancienne (sans famille) reste utilisable : chaque article est alors sa propre famille jusqu'au prochain chargement. Le catalogue se recopie à chaque ouverture de la vente et à la connexion (déjà en place).

### FMT-10 Cohérence et droits
Un autre commerce ne voit ni familles, ni formats, ni photos ; l'identifiant d'une famille ou d'une photo d'un autre commerce est introuvable (404). Le rôle conserve ses droits : le caissier lit le catalogue, ne crée ni ne modifie rien.

## Scénarios d'acceptation

1. **Eau minérale.** Le gérant crée « Eau minérale » avec 1,5 L (400 F), 1 L (300 F), 30 cl (150 F), chacun avec son stock. À la caisse, il tape « eau » : une seule famille ; il touche « 1 L » : le panier montre « Eau minérale · 1 L » à 300 F.
2. **Scan.** Le code-barres du 1,5 L ajoute directement « Eau minérale · 1,5 L ». Le stock des deux autres formats ne bouge pas.
3. **Sac de riz.** « Riz parfumé » en 5 kg, 25 kg, 50 kg, prix et prix d'achat différents ; la marge de chaque vente se calcule avec le coût du format vendu.
4. **Libellé en double.** Ajouter un second « 1 L » à « Eau minérale » est refusé avec un message clair ; « 1 l » aussi.
5. **Famille à un format.** « Sel fin » sans libellé s'ajoute d'un toucher ; lui ajouter un deuxième format oblige à donner un libellé au premier.
6. **Photo facultative.** Une famille sans photo s'affiche avec ses initiales et se vend normalement. Un fichier PDF renommé en `.jpg` est refusé. Une image de 5 Mo est réduite par l'application avant l'envoi ; si elle reste trop lourde pour le serveur, un message l'explique.
7. **Hors ligne.** Réseau coupé : recherche, choix du format et vente fonctionnent ; les photos déjà vues s'affichent, les autres montrent les initiales.
8. **Format épuisé.** Le bouton du format à zéro dit « Épuisé » en toutes lettres et applique la règle de stock du commerce.
9. **Reprise.** Après mise en service, tous les produits d'avant sont là, mêmes prix et mêmes stocks, chacun dans sa famille.
10. **Regroupement.** Regrouper « Eau 1,5 L » et « Eau 50 cl » en « Eau minérale » : les ventes du passé gardent leur ancien nom, le catalogue montre « Eau minérale · 1,5 L » et « Eau minérale · 50 cl ».
11. **Désactivation.** Désactiver le seul format restant retire la famille de la caisse ; l'historique reste lisible.
12. **Isolation.** Le commerce B ne voit jamais les familles ni les photos du commerce A, même avec l'identifiant exact.
13. **Rejeu.** Le même envoi de création rejoué après une coupure réseau ne crée ni deux familles ni deux formats.
14. **Droits.** Le caissier ne peut ni créer, ni modifier, ni mettre une photo ; le magasinier non plus ; le serveur refuse (403) même si l'écran était contourné.

## Hors lot

- Achat par carton ou sac avec conversion en unités et coût unitaire (lot 2) ; vente au poids, sac ouvert, quantités décimales (lot 3).
- Autres caractéristiques que le libellé (couleur, taille…), import par fichier, étiquettes imprimées.
- Fournisseurs par format, prix différents selon le client ou l'heure (promotions).
- Photo prise à la caisse hors ligne et envoyée plus tard ; recadrage et retouche dans l'application.
- Regrouper les statistiques (meilleures ventes, tableau de bord) par famille : elles restent **par format** avec le nom affiché.
