# 0016 — Étiquettes de produits et tickets de caisse

**Statut :** proposé le 25/10/2026, à valider avec le porteur du projet (qui a délégué le choix). Complète `0015` (lecteur de codes-barres) et `docs/specs/formats-produits`.

## Contexte

Le commerçant va équiper sa caisse d'un lecteur de codes-barres. Deux manques apparaissent :
1. Beaucoup d'articles **n'ont pas de code-barres** (riz au sac, pain, vrac, produits locaux) : le lecteur ne sert à rien pour eux, et l'application ne sait pas en fabriquer.
2. L'application **n'imprime aucun ticket** : le client n'a pas de preuve d'achat.

Contraintes du projet : application web (PWA) sans installation, utilisable hors ligne à la caisse, commerçants non techniciens, matériel d'impression inconnu à ce jour, pas de SMS ni de WhatsApp (décision du projet), numérotation des tickets sans trou hors ligne encore **non résolue** (RG-13 / OP-1 de `docs/analyse-merise.md`).

## Décisions

1. **Deux lots, dans cet ordre** : (1) étiquettes avec codes internes, (2) tickets de caisse. Les étiquettes débloquent le scan pour tout le catalogue ; le ticket est utile mais ne bloque pas la vente.
2. **Code interne = EAN-13 de la plage « usage en magasin »** (préfixe 29, réservée par GS1 à la circulation interne, donc sans risque de collision avec un produit du commerce). Il est fabriqué **par le serveur** (compteur par commerce, clé de contrôle calculée, unicité garantie par l'index existant, nouvel essai en cas de collision), jamais par l'appareil, pour que deux caisses ne créent pas le même code.
3. **Les codes-barres sont dessinés dans l'application** (EAN-13 pour les codes valides, Code 128 pour les autres), en SVG, par un petit encodeur à nous testé : pas de bibliothèque, fonctionne hors ligne, imprimable net.
4. **Impression par le navigateur** (`window.print` avec une mise en page CSS sur mesure), donc **toute imprimante installée sur l'appareil** convient : imprimante d'étiquettes, imprimante à tickets thermique 80 mm ou 58 mm, imprimante A4 classique. **Pas** d'envoi direct à l'imprimante (ESC/POS par USB ou Bluetooth) dans cette version : les navigateurs ne le permettent pas de façon fiable, et le pilote de l'imprimante fait déjà le travail.
5. **Deux formats d'étiquette** pour commencer : planche A4 (3 colonnes × 8 lignes, 70 × 37 mm) et rouleau 50 × 30 mm. Chaque étiquette : nom (et format), prix, code-barres, chiffres lisibles.
6. **Le ticket de caisse n'est pas une facture** : il le dit. Faute de numérotation sans trou hors ligne, il porte une **référence lisible** (date, heure, caisse, numéro de la vente sur la caisse ce jour-là), jamais un « numéro de ticket » qui laisserait croire à une numérotation légale. La numérotation officielle reste un chantier à part.
7. **Le ticket se fabrique à partir de ce que la caisse sait déjà** (lignes, paiements, rendu, caissier, nom du commerce) : il fonctionne hors ligne. On peut **réimprimer le dernier ticket**.
8. **Le cadre réel des imprimantes** (ajouté le 25/10/2026 après remarque du porteur) : dans beaucoup de commerces, l'imprimante est une **petite imprimante thermique Bluetooth 58 mm reliée à un téléphone Android**, que le navigateur ne voit pas toujours dans sa fenêtre d'impression (elle passe par l'application de l'imprimante, par exemple RawBT). Le ticket existe donc **sous trois formes** : « Imprimer » (fenêtre d'impression de l'appareil), **« Partager »** (le menu de partage du téléphone : l'utilisateur choisit l'application de son imprimante, ou la messagerie du client s'il le souhaite) et **« Copier »** (texte à coller). C'est l'utilisateur qui choisit où va le ticket : **l'application n'envoie rien elle-même** (décision du projet : pas de canal WhatsApp ou SMS géré par l'application). Le ticket texte tient en **32 colonnes (58 mm)** ou **42 colonnes (80 mm)**.
9. **Hors lot** : ticket envoyé par e-mail ou WhatsApp, impression directe ESC/POS, logo, adresse et téléphone du commerce sur le ticket (viendront avec les réglages de la boutique), facture, réimpression d'un ancien ticket depuis l'historique, étiquettes de rayon à prix barré.

## Risques et limites

- Un code interne imprimé puis perdu ou effacé se **réimprime** (même code) ; il ne se régénère jamais pour un produit qui en a déjà un.
- Le ticket est écrit pour s'imprimer sans réglage sur une imprimante thermique ordinaire, mais je ne l'ai pas essayé avec **la** imprimante du commerçant : le premier essai peut demander un ajustement (largeur utile, accents mal rendus par certains modèles).
- Le rendu exact (marges, taille) dépend de l'imprimante : le premier essai avec le vrai matériel pourra demander un réglage de marges. La mise en page est donc pensée avec des marges et des tailles ajustables en un seul endroit.
- Les codes internes commencent par 29 : un lecteur ou un autre logiciel ne les reconnaît pas hors de la boutique, ce qui est voulu.
