# 0007 — Prix d'achat moyen pondéré et coût figé à la vente

**Statut :** accepté le 24/10/2026 (RG-11 de l'analyse ; arbitrages recommandés par l'équipe, à valider avec le pilote).

## Contexte

Le prix d'achat d'un produit était écrasé à chaque réception, et la marge d'un classement utilisait le prix du jour : elle n'est exacte que si le prix n'a jamais changé.

## Décision

1. **Méthode du coût moyen pondéré** (RG-11), pas FIFO : simple à expliquer à un commerçant (« la moyenne de ce que j'ai payé »), sans suivi par lot, adaptée à la vente hors ligne où l'on ne sait pas de quel lot sort un article.
2. **Le prix d'achat de la fiche est le prix moyen.** Pas de second champ : un seul chiffre à lire et à corriger. La modification manuelle le remplace (correction, premier prix).
3. **Stock utile** = max(stock, 0) dans la moyenne : un stock négatif provient de ventes déjà valorisées ; le laisser peser fausserait le prix des nouvelles unités.
4. **Entiers en FCFA, arrondi au plus proche** (0,5 vers le haut). L'erreur par unité est inférieure à 0,5 FCFA ; elle est acceptée et ne s'accumule pas au-delà de ce que l'arrondi de chaque réception apporte.
5. **Coût figé à l'enregistrement par le serveur**, pas à l'heure de la vente sur la caisse : les ventes hors ligne arrivent plus tard et le serveur ne reconstitue pas le prix moyen d'un instant passé. Écart assumé : une vente hors ligne synchronisée après une réception retient le prix moyen d'après la réception.
6. **Ventes antérieures** : pas de rétro-calcul (les lignes de vente sont immuables en base). Leur marge reste estimée avec le prix actuel et est **signalée** comme telle ; la proportion d'estimé disparaît avec le temps.
7. **Réception sans prix** : le prix moyen ne change pas (comportement des clients existants).

## Risques et limites

- Un prix de livraison saisi à tort fausse la moyenne ; la correction manuelle de la fiche est tracée, l'historique garde le prix de chaque livraison.
- Annulation de vente : elle ne remet pas le coût (hors lot, règle de remboursement T017 non tranchée).
- La marge ne tient compte ni des frais de transport, ni des remises fournisseur.

## Réexamen

À la validation avec le pilote (faut-il FIFO pour les produits périssables ?).
