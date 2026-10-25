# Spécification — Étiquettes de produits et tickets de caisse

**Statut :** brouillon à valider ; décisions de `docs/decisions/0016-etiquettes-et-tickets.md`.
**Lots :** (1) codes internes et étiquettes ; (2) ticket de caisse. Chaque lot a ses tests, son code et sa vérification avant le suivant.

## Lot 1 — Codes internes et étiquettes

### ETQ-01 Créer un code interne
Dans la fiche d'un produit (ou d'un format) **sans code-barres**, un bouton « Créer un code interne » (propriétaire et gérant).
- Le serveur fabrique un **EAN-13 de préfixe 29** : `29` + 10 chiffres d'un compteur propre au commerce + clé de contrôle. `POST /api/v1/products/{id}/internal-barcode` répond le code.
- **Jamais deux fois** : si le produit a déjà un code (d'usine ou interne), la demande est refusée avec « Ce produit a déjà un code-barres » ; le code existant se réimprime.
- Unique dans le commerce (index existant) ; en cas de collision avec une autre caisse, le serveur recommence avec le numéro suivant.
- Journalisé (« Code interne créé : Riz parfumé · 25 kg »). Rien d'autre n'est modifié sur le produit.
- 403 pour les autres profils, 404 pour un produit d'un autre commerce.
- Le code créé est scannable à la caisse comme n'importe quel code (ADR 0015).

### ETQ-02 Le dessin du code-barres
L'application dessine le code en **SVG**, hors ligne :
- **EAN-13** si le code compte 13 chiffres et que sa clé de contrôle est juste ;
- **Code 128** pour tout autre code (alphanumérique, longueur quelconque, clé fausse) ;
- les **chiffres lisibles** sont écrits sous les barres ; une zone blanche (marge) entoure les barres, nécessaire à la lecture.
Un code que ni l'un ni l'autre ne peut dessiner (caractère hors Code 128) est signalé sur l'étiquette par « Code non imprimable » plutôt que dessiné faux.

### ETQ-03 L'étiquette
Nom du produit (et format sur une seconde ligne), **prix de vente en FCFA**, code-barres et chiffres. Un nom trop long est coupé proprement sur deux lignes puis tronqué par « … » ; le prix et le code ne sont jamais coupés.

### ETQ-04 L'écran « Étiquettes » (Gestion ; propriétaire, gérant, magasinier)
1. Liste des produits actifs avec recherche par nom ; case par produit ; **« Tout sélectionner »** ; filtre **« Sans code-barres seulement »**.
2. Pour chaque produit coché : nombre d'étiquettes (1 par défaut, 1 à 99).
3. Un produit coché **sans code** propose « Créer un code interne » sur place ; l'impression ne démarre pas tant qu'un produit coché n'a pas de code (« 3 produits n'ont pas de code : créez-leur un code interne »).
4. **Format** : « Feuille A4 — 24 étiquettes (3 × 8) » ou « Rouleau 50 × 30 mm ».
5. **Aperçu** à l'écran de ce qui sera imprimé, puis **« Imprimer »** : ouvre la fenêtre d'impression de l'appareil avec une mise en page fixe (une étiquette = une case de la planche, ou une étiquette = une page du rouleau). Sur planche, l'impression commence à la première case libre (champ « Commencer à l'étiquette n° », 1 par défaut, pour réutiliser une planche entamée).
6. En ligne seulement (la liste vient du serveur) ; hors ligne, message clair.

### ETQ-05 Rien d'autre ne change
Les prix, stocks et fiches ne sont jamais modifiés par l'impression.

## Lot 2 — Ticket de caisse

### TKT-01 Quand
À la fin d'une vente encaissée, l'écran propose **« Imprimer le ticket »** et « Nouvelle vente ». L'impression est facultative : ne pas imprimer ne retarde jamais la vente suivante. Un bouton **« Réimprimer le dernier ticket »** reste disponible sur l'écran de vente tant qu'aucune autre vente n'a été encaissée sur cet appareil depuis une fermeture de l'application.

### TKT-02 Le contenu
En haut : **nom du commerce**. Puis : date et heure, **caissier**, **référence** (TKT-03). Les lignes : nom (et format), quantité × prix unitaire, total de ligne. Puis : **TOTAL**, **moyens de paiement** avec leur montant, **somme reçue** et **monnaie rendue** pour les espèces. Vente à crédit : nom du client et « Reste à payer ». En bas : « Merci de votre visite » et « Ticket de caisse — ne vaut pas facture ». Tout en français, montants en FCFA avec séparateur de milliers.

### TKT-03 La référence
`25/10 14:32 · Caisse 1 · n° 12` : le numéro est le rang de la vente **sur cette caisse ce jour-là**, compté par l'appareil. Ce n'est **pas** une numérotation légale sans trou (chantier à part) ; le ticket ne l'appelle pas « numéro de ticket » ni « numéro de facture ».

### TKT-04 Hors ligne
Le ticket se compose avec les données de la vente connues de l'appareil : il s'imprime sans réseau.

### TKT-05 La mise en page
Largeur **80 mm** par défaut, **58 mm** au choix (réglage gardé sur l'appareil), texte noir sur blanc, police lisible, sans image ; la hauteur suit le contenu (pas de page A4 gâchée). L'impression passe par la fenêtre d'impression de l'appareil : toute imprimante installée convient.

### TKT-06 Rien n'est envoyé
Aucun ticket n'est envoyé par e-mail, WhatsApp ou SMS dans cette version.

## Scénarios d'acceptation

1. **Code interne.** Un produit sans code reçoit « 2900000000017 »-type ; son EAN-13 est valide ; le produit se retrouve en le scannant à la caisse.
2. **Pas de doublon.** Demander un second code interne pour le même produit est refusé ; deux produits n'ont jamais le même code, même créés au même instant depuis deux caisses.
3. **Droits.** Le caissier ne crée pas de code (403) ; le magasinier imprime mais ne crée pas ; un autre commerce reçoit 404.
4. **Dessin.** L'EAN-13 « 6111000000014 » est dessiné en EAN-13 ; « 111 » ou « RIZ-25 » en Code 128 ; un code à clé fausse (« 6111000000011 ») est dessiné en Code 128 ; chaque dessin se relit exactement (test aller-retour).
5. **Étiquettes d'une sélection.** 3 produits, 2 / 1 / 5 exemplaires : 8 étiquettes, dans l'ordre, sur 1 planche A4 ; avec « commencer à l'étiquette n° 20 » : 5 étiquettes sur la planche courante, 3 sur la suivante.
6. **Produit sans code coché.** L'impression est refusée avec le nombre de produits concernés ; après « Créer un code interne » elle devient possible.
7. **Rouleau.** Une étiquette = une page 50 × 30 mm.
8. **Ticket.** Une vente de 2 articles payée 5 000 en espèces donne un ticket avec lignes, total, reçu, monnaie, caissier, référence, mention « ne vaut pas facture ».
9. **Ticket à crédit.** Le ticket indique le client et le reste à payer.
10. **Hors ligne.** Le réseau coupé, le ticket s'imprime.
11. **Réimpression.** Le dernier ticket se réimprime à l'identique.
12. **Format 58 mm.** Le choix change la largeur du ticket et reste après rechargement.

## Hors lot

Voir la décision 0016 (e-mail/WhatsApp, impression directe ESC/POS, logo et coordonnées du commerce, facture, anciens tickets, numérotation légale).
