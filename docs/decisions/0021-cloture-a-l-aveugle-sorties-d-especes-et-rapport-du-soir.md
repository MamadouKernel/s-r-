# 0021 — Clôture à l'aveugle, sorties d'espèces et rapport du soir

**Statut :** proposé le 27/10/2026, à valider avec le porteur du projet. Complète `0020` (les remboursements en espèces sortent déjà du tiroir), `0018` (validation d'un gérant) et `0011` (envoi d'e-mails d'exploitation). Réalise RG-07 (écart signalé), RG-24 (montant attendu) et la décision « rapport du soir » de `docs/analyse-merise.md`.

## Ce que disent les pratiques réelles du commerce de détail

Cette décision s'appuie sur des pratiques documentées (recherche du 27/10/2026), pas sur des cas inventés. Ce qui est établi, et ce qui ne l'est pas :

- **Compter sans voir le total attendu est une pratique courante**, avec le détail par coupure noté avant que le total attendu soit révélé ([Shopify, équilibrer un tiroir-caisse](https://www.shopify.com/blog/balancing-a-cash-drawer) ; [modèles de contrôle de caisse MangoApps](https://www.mangoapps.com/templates/inspections/cashier-cash-handling-certification-and-variance-threshold)). Un guide de caisse recommande aussi que **la personne qui compte ne soit pas celle qui a vendu toute la journée** ([guide de caisse, IUT de Thionville-Yutz](https://iut-thionville-yutz.univ-lorraine.fr/files/2024/09/hotesse-de-caisse-niveau-2.pdf)).
- **Il n'existe pas de tolérance universelle** : aucune source ne donne de chiffre ; la tolérance est une politique du commerce ([Fast Casual, « votre tolérance aux manquants »](https://www.fastcasual.com/blogs/your-tolerance-for-cash-shortages-bearable-or-terrible)). Le **recomptage, l'enquête et la remontée au-dessus du seuil** sont la règle, et **les excédents méritent autant d'attention que les manquants**, car ils peuvent cacher une mauvaise gestion de l'argent.
- **Les entrées et sorties d'espèces doivent être notées pour ne pas passer pour des manquants**, et **l'écart se note par tiroir et par équipe**, dans un compte à part plutôt que lissé ou noyé dans les frais divers ([MangoApps](https://www.mangoapps.com/templates/sop/retail-daily-sales-reconciliation-sop) ; [Beancount, écarts de caisse](https://beancount.io/fr/blog/2026/07/08/cash-over-and-short-account-till-discrepancy-bookkeeping)).
- **Les logiciels de caisse limitent l'argent qui s'accumule dans le tiroir** : au-delà d'un seuil, ils demandent un versement au coffre, avec un avertissement puis un plafond qui bloque ([Verifone, safe drop](https://docs.verifone.com/cug-57.00/commander-user-reference-57.00/safe-drop-safe-loan) ; [CBS NorthStar](https://cbsnorthstar.atlassian.net/wiki/spaces/ORDERENTRY/pages/2643656731) ; [NCR Aloha](https://docs.ncrvoyix.com/restaurant/aloha-cloud/implementing/settings/configuring_pay_in_out_codes)) ; trop d'argent dans un tiroir est un risque de vol ([Bank of America](https://merchanthelp.bankofamerica.com/Back-Office-Cash-Management-for-Retail-Solution)). Le versement se note à part d'une dépense.
- **Faire rembourser les manquants aux employés est déconseillé et parfois illégal** selon les pays ([Fast Casual](https://www.fastcasual.com/blogs/your-tolerance-for-cash-shortages-bearable-or-terrible)). Sôrô ne calcule donc aucune retenue : il montre l'écart, il ne juge pas. Ce qui est permis en Côte d'Ivoire est **à vérifier auprès d'un juriste du travail** avant tout usage.
- **Ce que je n'ai pas pu établir** : aucune source trouvée sur les pratiques de caisse des petites boutiques ivoiriennes (cahier de dépenses, contrôle des caissiers). Tout ce qui est propre à la Côte d'Ivoire ci-dessous est une **hypothèse à confirmer avec les commerçants du pilote** (T030). Pour le Mobile Money, la Banque mondiale confirme son poids dans le pays ([Banque mondiale](https://worldbank.org/en/country/cotedivoire/publication/financial-services-in-cote-divoire-banks-set-aside-in-favor-of-mobile-money)) : l'argent reçu par Wave ou Orange n'est **pas dans le tiroir**, il se vérifie sur le téléphone du patron.

## Contexte

Le soir, le patron veut savoir **si l'argent est dans le tiroir** sans rester à surveiller. Aujourd'hui :

- à la clôture, la caisse **affiche le montant attendu avant que le caissier compte** : un caissier malhonnête n'a qu'à garder la différence en plus et « compter » exactement ce qu'on lui demande ; un caissier honnête n'a aucune raison de ne pas recompter ;
- il n'existe **aucune sortie d'espèces** : acheter un sachet, payer le transport du livreur, remettre l'argent au patron en cours de journée ne se saisit nulle part, donc **chaque dépense honnête crée un faux manque** à la clôture ;
- l'écart est enregistré, mais **personne n'est prévenu** et une caisse oubliée ouverte ne se voit que si on ouvre le tableau de bord ;
- le patron n'a **pas de compte rendu** de la journée : il doit ouvrir l'application.

## Décisions

1. **Clôture à l'aveugle** : le caissier **compte d'abord**, sans voir le montant attendu, ni avant ni après. Un **gérant peut compter à sa place** (il n'a pas vendu toute la journée) : c'est même ce qu'on conseille quand il est présent. Il ne voit jamais l'écart. Le gérant et le propriétaire le voient (écran Sessions, tableau de bord, rapport du soir).
2. **Un seul recomptage, sans dire de combien** : si l'écart dépasse la **tolérance**, la caisse répond « Votre comptage ne correspond pas à ce qui est attendu. Recomptez, billet par billet, puis confirmez. » (sans montant, sans sens, manque ou excédent). Le **second comptage est définitif**. Les deux comptages sont gardés. La tolérance par défaut est de **500 FCFA**, une **valeur de départ sans source** (aucune tolérance universelle n'existe) : elle se règle avec les commerçants du pilote.
3. **Aide au comptage, facultative** : « Compter par billets » propose les coupures et pièces du FCFA (10 000, 5 000, 2 000, 1 000, 500 en billets ; 500, 250, 200, 100, 50, 25, 10, 5 en pièces) et fait la somme. Le détail est gardé avec la clôture. Taper directement un total reste possible.
4. **Sorties et entrées d'espèces en cours de journée** (« Mouvements de caisse »), saisies sur la caisse, rattachées à la session ouverte : **Dépense** (motif obligatoire : transport, sachets et emballages, eau ou électricité, repas, autre), **Versement au coffre** (le gérant ou le patron prend de l'argent), **Apport de monnaie** (de l'argent entre dans le tiroir). Montant attendu = `fond + espèces encaissées − espèces remboursées − dépenses − versements + apports`.
5. **Garde-fou sur les sorties** : une sortie d'**au plus 5 000 FCFA** (seuil modifiable) se fait seul ; **au-delà**, un gérant valide avec son PIN (même mécanisme que `0020` : preuve signée, liée au mouvement et au montant). Sans réseau, une petite sortie passe ; une grosse attend le réseau. Un mouvement n'est jamais supprimé : on l'annule par un mouvement inverse motivé.
6. **Écart au-delà de la tolérance = signalé, manque comme excédent** : journal (« Écart de caisse de −1 500 FCFA, session de Awa »), tableau de bord, rapport du soir. Un écart ne se corrige jamais après coup et ne se perd pas dans les frais : il est rangé à part. **Sôrô ne calcule aucune retenue sur salaire.**
6bis. **Trop d'argent dans le tiroir** : au-dessus d'un seuil (valeur de départ : **100 000 FCFA** attendus), la caisse **avertit** « Faites un versement au coffre » ; elle ne bloque rien dans ce lot (un blocage se décidera après le pilote). Le versement est noté à part d'une dépense.
7. **Caisse oubliée ouverte** : une session ouverte depuis plus de **14 heures** est signalée au gérant, au propriétaire et dans le rapport (« La caisse de Awa est ouverte depuis hier 08:00 »). Rien ne la ferme de force.
8. **Rapport du soir** : un e-mail au propriétaire chaque soir (le comptable lit la page de l'application : il n'a pas d'adresse e-mail dans Sôrô) à **20 h** (heure du pays), et la même page dans l'application (« Rapport du jour », avec choix de la date). Contenu : chiffre d'affaires et nombre de tickets ; paiements par moyen, avec pour le **Mobile Money** le total à retrouver sur le téléphone du patron (cet argent n'est pas dans le tiroir) ; **par caissier** : ventes, annulations, retours, remises, écart de caisse ; sorties d'espèces avec leurs motifs ; sessions non clôturées ; dettes créées et encaissées ; produits en rupture ou sous le seuil. Aucun chiffre n'est interprété : le patron juge. Un jour sans vente ni caisse ouverte n'envoie rien.
9. **Hors lot** : photo du justificatif de dépense, rapport par WhatsApp ou SMS, heure d'envoi par commerce, inventaire de caisse « surprise » demandé par le patron, rapprochement avec la banque, plusieurs tiroirs par session.

## Risques et limites

- Un caissier peut encore **sortir de l'argent sans le saisir** : l'écart apparaîtra à la clôture, pas avant. L'aveugle ne protège pas contre un vol de chaque soir très régulier ; il rend l'écart **visible** et attribuable à une personne et à un jour.
- Un gérant qui valide **ses propres complices** reste possible ; chaque validation est journalisée avec son nom.
- Un second comptage donne l'occasion de **retrouver exactement** le montant attendu en cherchant ; c'est pourquoi la caisse ne dit jamais le sens ni le montant, et pourquoi le premier comptage est gardé.
- Le rapport par e-mail dépend du serveur d'envoi (`0005`) : s'il est coupé, la page de l'application reste la référence.

## Réexamen

Après le pilote : tolérance, seuil de validation des sorties, heure d'alerte d'une caisse ouverte, heure et canal du rapport.
