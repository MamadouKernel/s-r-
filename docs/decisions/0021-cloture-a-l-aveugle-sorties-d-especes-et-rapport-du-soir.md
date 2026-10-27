# 0021 — Clôture à l'aveugle, sorties d'espèces et rapport du soir

**Statut :** proposé le 27/10/2026, à valider avec le porteur du projet. Complète `0020` (les remboursements en espèces sortent déjà du tiroir), `0018` (validation d'un gérant) et `0011` (envoi d'e-mails d'exploitation). Réalise RG-07 (écart signalé), RG-24 (montant attendu) et la décision « rapport du soir » de `docs/analyse-merise.md`.

## Contexte

Le soir, le patron veut savoir **si l'argent est dans le tiroir** sans rester à surveiller. Aujourd'hui :

- à la clôture, la caisse **affiche le montant attendu avant que le caissier compte** : un caissier malhonnête n'a qu'à garder la différence en plus et « compter » exactement ce qu'on lui demande ; un caissier honnête n'a aucune raison de ne pas recompter ;
- il n'existe **aucune sortie d'espèces** : acheter un sachet, payer le transport du livreur, remettre l'argent au patron en cours de journée ne se saisit nulle part, donc **chaque dépense honnête crée un faux manque** à la clôture ;
- l'écart est enregistré, mais **personne n'est prévenu** et une caisse oubliée ouverte ne se voit que si on ouvre le tableau de bord ;
- le patron n'a **pas de compte rendu** de la journée : il doit ouvrir l'application.

## Décisions

1. **Clôture à l'aveugle** : le caissier **compte d'abord**, sans voir le montant attendu, ni avant ni après. Il ne voit jamais l'écart. Le gérant et le propriétaire le voient (écran Sessions, tableau de bord, rapport du soir).
2. **Un seul recomptage, sans dire de combien** : si l'écart dépasse la **tolérance**, la caisse répond « Votre comptage ne correspond pas à ce qui est attendu. Recomptez, billet par billet, puis confirmez. » (sans montant, sans sens, manque ou excédent). Le **second comptage est définitif**. Les deux comptages sont gardés. La tolérance par défaut est de **500 FCFA** (une petite erreur de monnaie), modifiable plus tard dans les paramètres du commerce.
3. **Aide au comptage, facultative** : « Compter par billets » propose les coupures et pièces du FCFA (10 000, 5 000, 2 000, 1 000, 500 en billets ; 500, 250, 200, 100, 50, 25, 10, 5 en pièces) et fait la somme. Le détail est gardé avec la clôture. Taper directement un total reste possible.
4. **Sorties et entrées d'espèces en cours de journée** (« Mouvements de caisse »), saisies sur la caisse, rattachées à la session ouverte : **Dépense** (motif obligatoire : transport, sachets et emballages, eau ou électricité, repas, autre), **Versement au coffre** (le gérant ou le patron prend de l'argent), **Apport de monnaie** (de l'argent entre dans le tiroir). Montant attendu = `fond + espèces encaissées − espèces remboursées − dépenses − versements + apports`.
5. **Garde-fou sur les sorties** : une sortie d'**au plus 5 000 FCFA** (seuil modifiable) se fait seul ; **au-delà**, un gérant valide avec son PIN (même mécanisme que `0020` : preuve signée, liée au mouvement et au montant). Sans réseau, une petite sortie passe ; une grosse attend le réseau. Un mouvement n'est jamais supprimé : on l'annule par un mouvement inverse motivé.
6. **Écart au-delà de la tolérance = signalé** : journal (« Écart de caisse de −1 500 FCFA, session de Awa »), tableau de bord, rapport du soir. Un écart ne se corrige jamais après coup.
7. **Caisse oubliée ouverte** : une session ouverte depuis plus de **14 heures** est signalée au gérant, au propriétaire et dans le rapport (« La caisse de Awa est ouverte depuis hier 08:00 »). Rien ne la ferme de force.
8. **Rapport du soir** : un e-mail au propriétaire (et au comptable s'il existe) chaque soir à **20 h** (heure du pays), et la même page dans l'application (« Rapport du jour », avec choix de la date). Contenu : chiffre d'affaires et nombre de tickets ; paiements par moyen ; **par caissier** : ventes, annulations, retours, remises, écart de caisse ; sorties d'espèces avec leurs motifs ; sessions non clôturées ; dettes créées et encaissées ; produits en rupture ou sous le seuil. Aucun chiffre n'est interprété : le patron juge. Un jour sans vente ni caisse ouverte n'envoie rien.
9. **Hors lot** : photo du justificatif de dépense, rapport par WhatsApp ou SMS, heure d'envoi par commerce, inventaire de caisse « surprise » demandé par le patron, rapprochement avec la banque, plusieurs tiroirs par session.

## Risques et limites

- Un caissier peut encore **sortir de l'argent sans le saisir** : l'écart apparaîtra à la clôture, pas avant. L'aveugle ne protège pas contre un vol de chaque soir très régulier ; il rend l'écart **visible** et attribuable à une personne et à un jour.
- Un gérant qui valide **ses propres complices** reste possible ; chaque validation est journalisée avec son nom.
- Un second comptage donne l'occasion de **retrouver exactement** le montant attendu en cherchant ; c'est pourquoi la caisse ne dit jamais le sens ni le montant, et pourquoi le premier comptage est gardé.
- Le rapport par e-mail dépend du serveur d'envoi (`0005`) : s'il est coupé, la page de l'application reste la référence.

## Réexamen

Après le pilote : tolérance, seuil de validation des sorties, heure d'alerte d'une caisse ouverte, heure et canal du rapport.
