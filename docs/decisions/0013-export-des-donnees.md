# 0013 — Export des données du propriétaire

**Statut :** proposé le 25/10/2026, à valider avec le porteur du projet. Lot 3 de l'abonnement (docs/decisions/0011, ABO-10).

## Contexte

Un commerçant confie sa comptabilité du quotidien à Sôrô. Il doit pouvoir **repartir avec ses données** à tout moment, y compris quand son abonnement est terminé : c'est ce qui rend la lecture seule acceptable (elle ne prend personne en otage) et ce qui rassure avant de s'abonner.

## Décisions

1. **Un classeur Excel (.xlsx)**, pas de CSV : un seul fichier, plusieurs feuilles, des dates et des montants que le commerçant sait lire, et un format qu'il ouvre déjà (comme pour l'import, 0010). Le CSV et l'export comptable normalisé sont hors lot.
2. **Propriétaire seul.** Le fichier contient les prix d'achat, les marges implicites et les numéros de téléphone des clients ; le gérant n'en a pas besoin et le propriétaire est responsable de ces données.
3. **Toujours disponible** : essai, actif, grâce et **lecture seule** (c'est une lecture, le garde-fou de l'abonnement ne la bloque pas). Il ne dépend jamais d'un paiement.
4. **Ce qui est exporté** : produits (avec familles et formats), clients et dettes, versements, ventes, lignes de vente, mouvements de stock. **Jamais** : PIN, mots de passe, jetons, appareils, journal technique, photos.
5. **Aucun identifiant technique dans le fichier** : une vente se retrouve par sa date, son numéro de ticket et son caissier, jamais par un code interne.
6. **Une période pour l'historique** (ventes, versements, mouvements), 12 mois par défaut ; produits et clients sont toujours complets, car ce sont des états, pas des historiques.
7. **Borné** : au plus 100 000 lignes au total, vérifié avant de fabriquer le fichier ; au-delà, le message demande une période plus courte. Pas de fichier géant en mémoire.
8. **Les valeurs seulement, jamais de formule**, comme à l'import : un texte qui commence par `=`, `+`, `-` ou `@` reste du texte.
9. **Tracé et limité** : chaque export est écrit au journal (qui, quand, période, nombre de lignes) et limité à 5 par heure et par boutique. Ce fichier est la copie la plus complète des données du commerce : il mérite une trace.
10. **Heure de Côte d'Ivoire** : le pays n'a pas d'heure d'été et suit l'heure UTC ; les dates du fichier sont donc affichées telles quelles, et le dit la feuille « Lisez-moi ».

## Risques et limites

- Le fichier circule hors de Sôrô (téléphone, clé USB, e-mail) : l'écran avertit qu'il contient des données personnelles de clients et des prix d'achat.
- Un export très gros ralentit le serveur : d'où la borne, la limite de fréquence et la fabrication à la demande.
- La date d'un export n'est pas une sauvegarde : elle ne remplace pas les sauvegardes du serveur (0012) et ne permet pas de réimporter les ventes.
