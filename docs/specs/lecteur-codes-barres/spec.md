# Spécification — Lecteur de codes-barres universel

**Statut :** brouillon à valider ; décisions de `docs/decisions/0015-lecteur-de-codes-barres-universel.md`.
**Lots :** (1) reconnaissance des scans et caisse ; (2) fiche produit et écran « Lecteur de codes-barres ». Application seulement, aucun changement serveur.

## Objectif

Un commerçant branche n'importe quel lecteur de codes-barres en mode clavier (USB, Bluetooth, 2,4 GHz ; 1D ou 2D) : **un scan ajoute le produit au panier**, sans réglage du lecteur, sans cliquer dans une zone, avec un retour qu'on entend et qu'on voit.

## Exigences

### SCN-01 Reconnaître un scan
Une **rafale** d'au moins 6 caractères dont chacun suit le précédent à moins de 40 ms est un scan. Elle se termine par Entrée, Tab, ou 100 ms sans caractère. Une saisie humaine (plus lente) n'est **jamais** prise pour un scan, et la recherche par nom, la saisie d'un code à la main puis Entrée fonctionnent comme aujourd'hui.

### SCN-02 Capté sans focus (écran de vente)
Si aucune zone de saisie n'a le focus, un scan est capté quand même. Si une zone de saisie a le focus, le scan s'y écrit comme une saisie ; dans la zone « Rechercher ou scanner », il ajoute le produit (comportement actuel, maintenu). Un scan ne remplit jamais une autre zone de saisie à la place de l'utilisateur à la caisse (champ de montant, de remise, d'étiquette de mise en attente).

### SCN-03 Terminaisons et parasites
Entrée, Tab et « rien » sont acceptés. Les caractères de contrôle et les espaces en début ou fin de code sont ignorés (préfixe et suffixe ajoutés par certains lecteurs).

### SCN-04 Clavier décalé
Pendant un scan, un chiffre est lu d'après la **touche physique** (`Digit0`–`Digit9`, `Numpad0`–`Numpad9`). Un lecteur « américain » sur un clavier français donne donc « 6111000000011 » et non « é&&&àààààààèè ». Pour les autres caractères (lettres, tirets d'un code alphanumérique), on garde le caractère produit.

### SCN-05 Caisse : le retour
- **Code connu** (produit actif de la copie locale du catalogue, code-barres exact) : ajouté au panier (+1 s'il y est déjà), **bip aigu court**, message « Jus d'orange 1L ajouté » annoncé aussi aux lecteurs d'écran, la zone de recherche reste vide.
- **Code inconnu** : **bip grave**, message « Aucun produit pour le code 123… », rien n'est ajouté.
- Les règles d'ajout existantes s'appliquent (produit désactivé, stock, plafond), avec leurs messages.
- Le son se coupe et se remet depuis l'écran « Lecteur de codes-barres » ; le choix est gardé sur l'appareil. Le son ne démarre qu'après un premier geste de l'utilisateur dans la page (règle des navigateurs) ; sans cela, le retour visuel suffit.

### SCN-06 Fiche produit
Dans la zone « Code-barres » d'un produit ou d'un format, un scan remplit le code. Entrée ou Tab envoyés par le lecteur **n'enregistrent pas** le formulaire et ne sautent pas de zone. Un code déjà pris par un autre produit est refusé avec le message actuel du serveur (« code-barres déjà utilisé »).

### SCN-07 Écran « Lecteur de codes-barres » (Gestion, propriétaire et gérant)
Explique en deux phrases comment brancher, puis une zone « Scannez un code ici ». Après un scan, il affiche :
- le **code lu**, sa **longueur** ;
- la **fin de lecture** : « Entrée », « Tab » ou « aucune (reconnu par la vitesse) » ;
- la **vitesse** : « très rapide », « rapide », « lente — risque de ne pas être reconnu » ;
- le **diagnostic** en français :
  - « Votre lecteur est bien reconnu. »
  - « Les chiffres sont décalés (le clavier du lecteur ne correspond pas) : l'application les corrige automatiquement. »
  - « Rien n'arrive : le lecteur n'est peut-être pas en mode clavier. Consultez sa notice : cherchez « mode clavier », « USB HID » ou « keyboard ». »
  - « Plusieurs lectures d'affilée pour un seul scan : réglez le lecteur en lecture manuelle (un appui = une lecture). »
- le bouton « Son du lecteur : activé / désactivé ».
Les anciens scans de la session sont listés (les 5 derniers), jamais gardés après fermeture.

### SCN-08 QR codes
Le texte d'un QR code est reçu comme un code ; à la caisse il est « inconnu » sauf s'il correspond exactement à un code-barres de produit. Aucune adresse n'est ouverte, aucun texte n'est interprété.

### SCN-09 Hors ligne et vie privée
Tout fonctionne hors ligne avec la copie locale du catalogue. Aucun code scanné n'est envoyé au serveur par ce lot (les ventes se synchronisent comme avant) ni gardé hors du journal habituel de la vente.

### SCN-10 Accessibilité
Chaque retour est une phrase (région annoncée), jamais seulement un son ou une couleur. Les boutons ont une cible d'au moins 44 px.

## Scénarios d'acceptation

1. **Lecteur avec Entrée.** Le champ de recherche a le focus ; le lecteur envoie le code et Entrée : le produit est ajouté, bip aigu, message.
2. **Lecteur sans Entrée.** Aucun terminateur : 100 ms après la rafale, le produit est ajouté.
3. **Sans focus.** Rien n'a le focus sur la vente : le scan ajoute le produit.
4. **Champ de montant.** Le focus est dans un champ de montant : le scan s'y écrit, le panier ne change pas (c'est à l'utilisateur de décider).
5. **Saisie humaine.** Taper « 6111… » à la main, lentement, puis Entrée : comme aujourd'hui ; taper un nom : la liste se filtre.
6. **Clavier décalé.** Un lecteur américain sur clavier français : « 6111000000011 » est retrouvé malgré les touches décalées.
7. **Préfixe et suffixe.** Un code précédé d'un caractère de contrôle et suivi d'un espace est retrouvé.
8. **Code inconnu.** Bip grave, message avec le code, panier inchangé.
9. **Deux fois le même produit.** Deux scans : quantité 2.
10. **Fiche produit.** Un scan remplit le code-barres sans enregistrer ni sauter de zone ; un code déjà pris est refusé avec le message du serveur.
11. **Écran de test.** Un scan affiche code, longueur, fin de lecture, vitesse et « Votre lecteur est bien reconnu » ; sans scan au bout de 15 s : le message « Rien n'arrive… » s'affiche.
12. **QR.** Un QR contenant une adresse web donne « inconnu » à la caisse, sans rien ouvrir.
13. **Hors ligne.** Le réseau coupé, les scénarios 1 à 9 fonctionnent.

## Hors lot

- Scan par la **caméra** du téléphone (autre chantier : dépend des navigateurs).
- Impression d'étiquettes et de codes-barres ; création de codes-barres internes.
- Lecteurs en mode série / USB COM / SDK propriétaire.
