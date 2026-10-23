# Spécification : écrans de gestion des produits et du stock (V1)

**Statut :** brouillon ; s'appuie sur `docs/specs/catalogue-stock/spec.md` (serveur, déjà livré). Les maquettes ne sont pas validées avec le pilote
(T030) : structure et états fixés, détail visuel provisoire.

## Objectif

Donner au gérant, au propriétaire et au magasinier les écrans pour tenir le catalogue et le stock : créer et modifier un produit, le
désactiver, réceptionner une livraison, déclarer une perte, lire l'historique et repérer les produits à réapprovisionner.

## Décisions

- **En ligne seulement.** Ce sont des écrans de gestion, pas de caisse : ils parlent directement au serveur, qui reste l'arbitre (droits, unicité des
  codes-barres, mouvements de stock). Hors ligne, un message clair l'explique ; aucune saisie n'est mise en file.
- **Droits par profil, appliqués aussi par le serveur** : l'interface masque seulement ce que le profil ne peut pas faire.
  Propriétaire et gérant : tout. Magasinier : consulter, réceptionner, déclarer une perte, lire l'historique ; ni création ni modification de fiche.
  Les autres profils n'ont pas l'espace « Gestion ».
- **Rejeu sûr** : un identifiant (produit, mouvement) est créé à l'ouverture d'un formulaire et conservé en cas de nouvel essai ; un double appui ou un
  nouvel essai après une erreur réseau ne crée jamais deux produits ni deux mouvements.
- Le prix d'achat n'est montré qu'aux profils autorisés (le serveur ne l'envoie pas aux autres).

## Exigences

### GES-01 Liste des produits
Recherche par nom ou code-barres exact ; filtre « Stock bas » (produits suivis au niveau du seuil ou en dessous, les plus urgents d'abord) ;
case « Afficher les produits désactivés » (propriétaire et gérant). Chaque ligne : nom, catégorie, prix de vente, prix d'achat (si visible), stock, seuil,
statut. Un produit en rupture ou sous le seuil est signalé **en toutes lettres**, jamais par la couleur seule.

### GES-02 Fiche produit
Création et modification : nom, code-barres, catégorie, prix d'achat, prix de vente, seuil d'alerte, suivi du stock (choisi à la création, non modifiable
ensuite). Montants en FCFA entiers. Un code-barres déjà utilisé (409) est signalé sous le champ concerné. Désactivation avec confirmation explicite
(le produit reste dans l'historique mais n'est plus proposé à la vente).

### GES-03 Réception
Quantité, fournisseur ou référence (texte libre), nouveau prix d'achat facultatif. Confirmation avec le nouveau stock.

### GES-04 Perte ou casse
Quantité et **motif obligatoire** ; la quantité ne peut pas dépasser le stock disponible (règle du serveur, rappelée à l'écran).

### GES-05 Historique
Mouvements d'un produit, du plus récent au plus ancien : date, type (réception, vente, perte, annulation de vente), quantité signée, motif, auteur (affiché par son identifiant tant que le serveur n'expose pas la liste des utilisateurs : prérequis de l'écran Équipe).

### GES-06 États et accessibilité
Chargement, vide, erreur et hors ligne sur chaque écran ; formulaires avec libellé persistant et erreur sous le champ ; cibles de 48 px ; utilisable à 320 px
sans défilement horizontal ; résultats annoncés aux technologies d'assistance.

## Scénarios d'acceptation

- **SC-G1** Un gérant crée un produit, le retrouve dans la liste, le modifie, puis le désactive après confirmation.
- **SC-G2** Un code-barres déjà pris affiche un message sous le champ et ne crée rien.
- **SC-G3** Un magasinier réceptionne 24 unités puis déclare une perte de 2 avec motif : le stock affiche 22 et l'historique montre les deux mouvements avec leur auteur.
- **SC-G4** Une perte sans motif ou supérieure au stock est refusée avec un message clair.
- **SC-G5** Un caissier n'a pas l'espace « Gestion » et n'accède pas aux adresses de gestion.
- **SC-G6** Un double appui sur « Enregistrer » ne crée qu'un produit.
- **SC-G7** Hors ligne, l'écran l'explique et n'enregistre rien.

## Hors lot

Import et export Excel, photo du produit, catégories gérées comme entité, inventaire, marge, écran Équipe (le serveur n'a pas encore de lecture des utilisateurs).
