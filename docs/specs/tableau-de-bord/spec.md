# Spécification : tableau de bord du propriétaire (téléphone d'abord)

**Statut :** brouillon ; s'appuie sur `docs/specs/consultation-rapports/spec.md` (serveur, déjà livré) et sur la promesse du cahier des charges :
« le propriétaire voit tout en direct sur son téléphone, même à l'autre bout du pays ». Les maquettes ne sont pas validées avec le pilote (T030).

## Objectif

Répondre en dix secondes, sur un téléphone, à : combien j'ai vendu aujourd'hui, comment c'est payé, qui a encaissé, quelles caisses sont ouvertes ou ont un écart,
et est-ce que du stock manque.

## Décisions

- **Aucun changement serveur** : le tableau de bord n'utilise que des lectures déjà livrées (`/reports/activity`, `/register-sessions`, `/stock/alerts`, `/users/directory`).
- **« Aujourd'hui » = le jour calendaire local de l'appareil**, de minuit à maintenant. En Côte d'Ivoire le fuseau est UTC sans heure d'été, donc jour local et jour UTC coïncident ;
  le fuseau propre au commerce sera à déclarer dans les paramètres (T028) pour un autre pays.
- **Comparaison honnête** : on compare à *hier à la même heure*, pas à la journée d'hier entière, qui n'est pas comparable à une journée en cours.
- **En direct** : actualisation automatique toutes les 60 secondes tant que la page est visible et le réseau disponible, plus un bouton « Actualiser ». L'heure de la dernière mise à jour est toujours affichée.
- **En ligne seulement.** Si une actualisation échoue, les derniers chiffres restent affichés avec l'heure de leur lecture et un avertissement : jamais de chiffres périmés sans le dire.
- **Pas de cartes décoratives uniformes** (DESIGN.md §4, §7) : un chiffre d'affaires en grand, puis des lignes et des tableaux lisibles, qui défilent dans leur propre conteneur si besoin.
- **Droits** : propriétaire, gérant et comptable (ceux que le serveur autorise à lire les rapports). Le bloc stock n'est montré qu'à qui peut lire les alertes (le comptable ne le voit pas). Le comptable lit sans rien modifier.
- **Téléphone d'abord, jusque dans l'en-tête** : sur petit écran, le titre et la déconnexion tiennent sur une ligne et la navigation sur une seule ligne qui défile dans son propre conteneur, pour que le chiffre d'affaires soit visible sans défiler.
- **Atterrissage** : après la connexion, le propriétaire et le comptable arrivent sur le tableau de bord ; les autres profils gardent leur parcours (ouverture de caisse).

## Exigences

### TDB-01 Chiffres du jour
Chiffre d'affaires du jour (total net des ventes validées) en grand ; nombre de tickets ; panier moyen (total ÷ tickets, arrondi au FCFA, « — » sans vente) ; remises accordées ;
ventes annulées (nombre et montant, hors chiffre d'affaires).

### TDB-02 Comparaison
Chiffre d'affaires et nombre de tickets d'hier à la même heure, avec la variation en pourcentage en toutes lettres (« en hausse de 12 % », « en baisse de 8 % », « stable ») ;
« — » quand hier est à zéro. La couleur ne porte jamais seule le sens.

### TDB-03 Répartition des paiements
Par moyen de paiement (espèces, Orange Money, MTN MoMo, Moov Money, Wave, crédit) : montant, nombre et part du total en pourcentage, du plus important au plus faible.

### TDB-04 Suivi par caissier
Par caissier : nom, tickets et total encaissé, du plus important au plus faible. Les noms viennent de l'annuaire ; sans lui, l'identifiant court.

### TDB-05 Caisses
- **Caisses ouvertes** : caissier, depuis quelle heure, espèces attendues en caisse ; signale une caisse ouverte depuis plus de 12 heures (« à clôturer ? »).
- **Écarts du jour** : sessions clôturées aujourd'hui avec un écart, en toutes lettres (« Manque de 500 FCFA », « Excédent de 200 FCFA »), et le cumul des manques et des excédents.

### TDB-06 Stock
Nombre de produits au niveau de leur seuil d'alerte ou en dessous, avec le lien « Voir les produits » vers l'espace Gestion (pour les profils qui y ont accès).

### TDB-07 États et accessibilité
Chargement, journée sans vente, erreur partielle (un bloc en échec n'empêche pas les autres), hors ligne. Utilisable à 320 px sans défilement horizontal de la page ; cibles de 48 px ;
mise à jour annoncée discrètement aux technologies d'assistance ; `prefers-reduced-motion` respecté.

## Scénarios d'acceptation

- **SC-D1** Une journée avec trois ventes (deux en espèces, une Orange Money) affiche le bon chiffre d'affaires, le bon panier moyen et la bonne répartition en pourcentages.
- **SC-D2** Hier à la même heure : une hausse, une baisse, un chiffre identique et un hier à zéro donnent chacun la bonne phrase.
- **SC-D3** Une caisse ouverte depuis 14 heures est signalée ; une session clôturée avec -500 FCFA apparaît sous « Écarts du jour » avec le nom du caissier.
- **SC-D4** Une actualisation en échec garde les derniers chiffres, affiche leur heure et un avertissement.
- **SC-D5** Un caissier n'a ni le lien ni la page ; un comptable voit le tableau de bord sans le bloc stock.
- **SC-D6** Un bloc en erreur (par exemple le stock) n'empêche pas l'affichage des autres.
- **SC-D7** L'actualisation automatique s'arrête quand la page est masquée ou quittée et ne lance jamais deux lectures en même temps.
- **SC-D8** Après connexion, un propriétaire arrive sur le tableau de bord, un caissier sur l'ouverture de caisse.

## Hors lot (à venir)

Meilleures ventes et produits les plus rentables (demande un endpoint de classement), comparaison semaine et mois, journal des actions sensibles, rapport du soir par WhatsApp, alertes poussées, vue multi-points de vente, export Excel et PDF.
