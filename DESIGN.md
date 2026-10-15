# Design System : Sôrô

## 1. Thème et atmosphère

Une interface de travail directe, rassurante et énergique, adaptée aux commerces ivoiriens. Densité équilibrée pour voir beaucoup d'informations sans tasser les contrôles. Les écrans de caisse privilégient la vitesse, les chiffres lisibles et la continuité hors ligne; la gestion et les rapports privilégient la comparaison et la traçabilité. La marque peut évoquer la confiance et le mouvement commercial sans clichés de fintech.

## 2. Palette et rôles

- **Ivoire clair** `#F7F7F2` — fond principal doux.
- **Surface blanche** `#FFFFFF` — zones de saisie et panneaux importants.
- **Encre charbon** `#20231F` — texte principal, jamais noir pur.
- **Ardoise** `#626A63` — libellés secondaires et métadonnées.
- **Ligne sauge grisée** `#DDE2DA` — séparateurs et bordures.
- **Vert feuille Sôrô** `#287A52` — accent unique pour action primaire, sélection et focus; saturation modérée.
- **États** : succès `#287A52`, avertissement `#A86412`, erreur `#B43A32`, information `#426A82`; toujours accompagnés d'un libellé ou d'une icône explicite, jamais par la couleur seule.

## 3. Typographie

- Sans-serif lisible à formes ouvertes, par exemple **Source Sans 3**; vérifier sa disponibilité et prévoir une pile système robuste.
- Chiffres de prix et quantités alignés tabulairement; police monospace seulement pour références techniques et horodatages denses.
- Hiérarchie par poids et taille, montants assez grands pour être lus à distance. Texte courant minimum 16 px sur caisse mobile.
- Libellés concis en français courant; FCFA sans décimales selon la règle produit.

## 4. Composants

- **Boutons** : cibles tactiles de 48 px minimum sur la caisse; action principale pleine et accentuée; état pressé visible, focus clavier net.
- **Touches rapides** : grandes surfaces à contraste clair, catégorie distincte, quantité et prix visibles sans ouvrir la fiche.
- **Panier** : lignes denses mais séparées, quantité éditable explicitement, total constamment visible sur grand écran et bien placé sur mobile.
- **Paiement** : modes clairement nommés, référence Mobile Money distincte du montant, récapitulatif final avant validation.
- **Bandeau réseau** : état « En ligne », « Hors ligne » ou « Synchronisation en attente » avec nombre d'opérations, jamais une simple icône.
- **Formulaires** : label persistant au-dessus du champ, aide facultative, erreur sous le contrôle.
- **Rapports** : tableaux et séparateurs plutôt que cartes décoratives; montants et écarts alignés.
- **États vides/erreurs** : expliquer la situation et l'action disponible. Chargements squelettiques cohérents; éviter les animations qui distraient pendant l'encaissement.

## 5. Mise en page responsive

- Mobile d'abord; caisse utilisable à 320 px sans défilement horizontal.
- À petite largeur, panier et paiement suivent le flux vertical; total et action de validation restent accessibles sans masquer le contenu.
- Desktop caisse : grille adaptée au scan rapide, catalogue et panier séparés; le panneau de paiement n'écrase pas le panier.
- Tablettes : commandes tactiles et marges confortables; gestion/rapports s'adaptent en une ou deux colonnes selon la largeur.
- Le propriétaire peut consulter les indicateurs sur téléphone; les tableaux larges deviennent des lignes lisibles ou défilent dans leur propre conteneur.

## 6. Mouvement et interactions

Mouvement bref et utile : changement d'état, ajout au panier, validation et synchronisation. Respecter `prefers-reduced-motion`; aucun mouvement perpétuel sur une caisse active. Éviter les délais et animations qui ralentissent l'encaissement. Les erreurs, succès et changements hors ligne sont annoncés aux technologies d'assistance.

## 7. Invariants UX

- Chaque validation financière donne une réponse claire et empêche le double tap.
- L'état local/en attente/synchronisé reste visible sans interrompre le service.
- Aucune action destructrice ambiguë; annulations et remises montrent le motif et l'autorisation requise.
- Couleur jamais seule porteuse de sens; focus clavier, contraste, libellés accessibles.
- Pas d'emojis, de néons, de dégradés décoratifs, de tableaux de bord à cartes uniformes, ni de texte d'interface générique.

## 8. Implémentation CSS : Tailwind

Le style repose sur Tailwind CSS, sans Bootstrap ni autre bibliothèque de composants préstylés. Les valeurs des sections 2 à 5 sont définies une seule fois dans le thème Tailwind (couleurs `ivoire`, `charbon`, `ardoise`, `sauge`, `feuille`, états; police Source Sans 3 avec pile système; tailles tactiles de 48 px) et les composants Angular n'utilisent que ces jetons, jamais de valeurs en dur. Le contraste, le focus clavier et `prefers-reduced-motion` restent obligatoires.

## 9. Parcours à concevoir avant le code

1. Connexion/PIN et ouverture de session.
2. Vente rapide au scan et à la recherche.
3. Paiement espèces, Mobile Money déclaré et paiement mixte.
4. Vente en attente et reprise.
5. Reconnexion, file en attente, conflit/rejet de synchronisation.
6. Clôture et explication d'écart.
7. Tableau de bord propriétaire sur téléphone.

Les écrans détaillés, contenus, états et prototypes sont à valider avec les caissiers du pilote. Le skill Stitch Design est disponible comme outil de conception; « UX/UI Pro » n'est pas présent dans les outils/plugins accessibles à cette session.
