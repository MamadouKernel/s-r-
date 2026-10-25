# Spécification — Export des données du propriétaire

**Statut :** brouillon à valider ; décisions de `docs/decisions/0013-export-des-donnees.md`. Lot 3 de l'abonnement (ABO-10 de `docs/specs/abonnement/spec.md`). S'appuie sur l'import Excel (`docs/specs/import-excel`) pour le format et la sûreté du fichier.
**Lots :** (1) serveur : fabrication du classeur, borne, journal, limite ; (2) écran « Exporter mes données ».

## Objectif

Le propriétaire télécharge **à tout moment**, même en lecture seule, un classeur Excel qui contient ses produits, ses clients et dettes, et l'historique de ses ventes, versements et mouvements de stock. Il repart avec ses données quand il veut.

## Exigences

### DON-01 Qui, quand
Réservé au **propriétaire** (403 pour tous les autres profils, gérant compris). Disponible pendant l'**essai**, l'abonnement **actif**, la **grâce** et la **lecture seule** : c'est une lecture, jamais bloquée par l'abonnement. Il faut le réseau (écran en ligne seulement).

### DON-02 Le contenu : sept feuilles, en français
1. **Lisez-moi** : nom de la boutique, date et heure de l'export, période choisie, nombre de lignes de chaque feuille, mention « heures de Côte d'Ivoire (UTC) » et avertissement sur les données personnelles.
2. **Produits** : Produit, Format, Catégorie, Code-barres, Prix d'achat, Prix de vente, Stock, Alerte stock, Suivi du stock (Oui/Non), État (Actif/Désactivé). Tous les produits, actifs ou non, **sans tenir compte de la période**. Le produit s'écrit comme à l'écran : « Eau minérale » et « 1,5 L » dans deux colonnes.
3. **Clients** : Nom, Téléphone, Plafond de crédit, Dette, État (Actif/Désactivé). Tous les clients.
4. **Versements** : Date et heure, Client, Montant, Moyen, Référence, Encaissé par. *(période)*
5. **Ventes** : Vente, Date et heure, N° de ticket, Caissier, Client (vente à crédit), Total brut, Remise, Total net, Moyens de paiement, État (Validée/Annulée), Motif d'annulation. *(période)*
6. **Lignes de vente** : Vente, Date et heure, N° de ticket, Produit (nom affiché), Quantité, Prix unitaire, Total, Coût unitaire retenu (vide si inconnu). *(période)*
7. **Mouvements de stock** : Date et heure, Produit, Type (Réception, Vente, Perte, Annulation de vente), Quantité (négative pour une sortie), Fournisseur ou motif, Coût unitaire, Fait par. *(période)*

La colonne **Vente** (feuilles 5 et 6) est un libellé lisible, par exemple « 12/10/2026 08:14 · ticket 12 · Awa Koné », qui permet de relier une ligne à sa vente avec un filtre Excel. **Aucun identifiant technique** (code interne, UUID) n'apparaît nulle part dans le fichier.

### DON-03 Ce qui n'est jamais exporté
PIN, mots de passe, jetons, secrets d'appareils, liste des appareils, journal technique des actions, photos, informations d'abonnement, données d'un autre commerce.

### DON-04 La période
`GET /api/v1/data-export?from=…&to=…` (dates en jours). Par défaut : les **12 derniers mois**. Choix proposés par l'écran : 1 mois, 3 mois, 12 mois, depuis le début, ou deux dates. La période s'applique aux feuilles 4 à 7 ; la fin comprend le jour entier. Début après la fin, ou plus de **5 ans** : 422 avec un message clair.

### DON-05 Taille bornée
Avant de fabriquer le fichier, le serveur **compte** les lignes à écrire. Au-delà de **100 000 lignes au total**, il répond 422 : « Il y a trop de données pour cette période (N lignes). Choisissez une période plus courte. » Rien n'est fabriqué dans ce cas.

### DON-06 Le fichier
Un classeur `.xlsx` nommé `soro-export-AAAA-MM-JJ.xlsx`. Dates en vraies dates Excel (`jj/mm/aaaa hh:mm`), montants en nombres entiers FCFA avec séparateur de milliers, colonnes à largeur lisible, première ligne figée, filtre automatique sur chaque feuille, une feuille vide garde ses titres. **Valeurs seulement, jamais de formule** : un texte qui commence par `=`, `+`, `-` ou `@` est écrit comme du texte. Les données sont lues en une seule lecture cohérente : une vente enregistrée pendant la fabrication n'apparaît pas à moitié.

### DON-07 Trace et limite de fréquence
Chaque export réussi est écrit au journal des actions (`data.exported` : qui, période, nombre de lignes par feuille) et visible dans la catégorie « Export de données ». **5 exports par heure et par boutique** ; au-delà, 429 : « Vous avez demandé plusieurs exports : réessayez dans N minutes. »

### DON-08 Droits et isolation
Propriétaire seul. Un autre commerce n'a aucune de ces lignes, quelle que soit la période. Le fichier ne contient que les données du commerce du propriétaire connecté.

### DON-09 Écran (lot 2)
Dans **Gestion → « Exporter mes données »** (propriétaire seulement) :
- une phrase qui dit ce qu'on obtient et que c'est **disponible même sans abonnement** ;
- le choix de la période ;
- la liste de ce que contient le fichier, en mots simples ;
- l'avertissement : « Ce fichier contient les numéros de téléphone de vos clients et vos prix d'achat. Gardez-le en lieu sûr. » ;
- « Télécharger l'export Excel », avec « Préparation du fichier… » pendant l'attente ;
- les erreurs en français (trop de données, trop d'exports, réseau).
En lecture seule, le bandeau du propriétaire propose aussi « Récupérer mes données ».

## Scénarios d'acceptation

1. **Export complet.** Une boutique avec 3 produits dont 2 formats, 2 clients, 10 ventes à crédit et comptant, 2 versements : le classeur a 7 feuilles ; Produits et Clients sont complets ; Ventes, Versements et Mouvements couvrent la période.
2. **Sans identifiant technique.** Aucune cellule du fichier ne contient un UUID ni un code interne ; une ligne de vente se relie à sa vente par la colonne « Vente ».
3. **Lecture seule.** Une boutique en lecture seule télécharge normalement son export ; une boutique en essai aussi.
4. **Droits.** Gérant, caissier, magasinier, comptable : 403.
5. **Période.** Début après la fin : 422 ; plus de 5 ans : 422 ; sans paramètre : 12 derniers mois ; la fin comprend le jour entier.
6. **Désactivés.** Un produit désactivé et un client désactivé figurent dans leur feuille avec « Désactivé ».
7. **Ventes annulées.** Elles figurent avec l'état « Annulée » et leur motif ; leurs lignes aussi.
8. **Marge.** Une ligne vendue avant le coût moyen a un « Coût unitaire retenu » vide, pas zéro.
9. **Formule.** Un client nommé « =1+1 » est écrit « =1+1 » comme texte, jamais calculé.
10. **Trop gros.** Plus de 100 000 lignes : 422 avec le nombre de lignes, aucun fichier.
11. **Limite.** Le 6ᵉ export de l'heure : 429 avec le délai.
12. **Journal.** Un export réussi laisse une entrée `data.exported` ; un export refusé n'en laisse pas.
13. **Isolation.** Le commerce B n'a jamais une ligne du commerce A.
14. **Écran.** Un propriétaire télécharge depuis l'écran ; un gérant ne voit pas la rubrique ; hors ligne, un message explique que le réseau est nécessaire.

## Hors lot

- CSV ; export planifié ou envoyé par e-mail ; export comptable normalisé (SYSCOHADA) ; PDF.
- Réimporter ventes et clients dans Sôrô ; restauration à partir d'un export (les sauvegardes du serveur s'en chargent, décision 0012).
- Export par le gérant ou le comptable ; export d'un seul type de données ; photos.
- Suppression des données après export.
