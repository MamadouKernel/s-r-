# 0020 — Annulations, remises et retours à la caisse

**Statut :** proposé le 26/10/2026, à valider avec le porteur du projet. Lève la question ouverte **T017** (effet d'une annulation sur l'argent en caisse et le stock) ; complète `0018` (approbation par le PIN d'un gérant) et `0017` (quantités au poids).

## Contexte

Dans une boutique réelle, trois choses arrivent tous les jours : le caissier **se trompe** (mauvais article, mauvais montant), le client **négocie** (« fais-moi un geste ») et le client **rapporte** un article. Aujourd'hui le serveur sait annuler une vente (réservé aux gérants) et accepter une remise, mais :

- l'annulation **ne remet rien en stock** et **ne rend pas l'argent** : le stock reste faux, et la caisse « attend » des espèces qui ne sont plus dans le tiroir (faux écart à la clôture) ;
- aucun écran ne permet d'annuler, de faire un retour ou de saisir une remise : tout passe par l'API ;
- un retour **partiel** (1 article sur 3) n'existe pas.

C'est exactement ce qui sert à voler (annuler un ticket encaissé et garder l'argent) ou à fausser les comptes. Il faut que ce soit **facile pour l'honnête homme et visible pour le patron**.

## Décisions

1. **Une vente n'est jamais supprimée ni modifiée** (RG-02) : on fait une **opération inverse** motivée (annulation ou retour), qui laisse la vente d'origine lisible.
2. **Annulation (toute la vente)** et **retour (une partie)** exigent **la validation d'un gérant, prouvée par son identifiant et son PIN** saisis sur la caisse du caissier (ADR 0018) : le gérant n'a pas à prendre le téléphone. Le propriétaire n'a pas de PIN : il fait l'opération lui-même sur la caisse. Le gérant qui agit lui-même n'a rien à prouver de plus. **Réseau obligatoire** (le serveur vérifie le PIN) : sans réseau, l'écran le dit et propose d'attendre ; la caisse ne devine jamais une validation.
3. **Effet sur le stock** : chaque article revient en stock (mouvement « Annulation de vente » ou « Retour ») **sauf** si le gérant dit que la marchandise est abîmée : elle part alors en **perte**, avec le même motif. Le choix se fait ligne par ligne, par défaut « remise en rayon ». Un produit au poids revient avec sa quantité exacte.
4. **Effet sur l'argent, dans la session de caisse ouverte au moment du remboursement** (pas dans celle de la vente, qui est peut-être clôturée) : le remboursement **en espèces** est une **sortie du tiroir** ; le montant attendu à la clôture devient `fond de caisse + espèces encaissées − espèces remboursées`. S'il n'y a pas de session ouverte, un remboursement en espèces est refusé (« Ouvrez la caisse pour rembourser en espèces ») ; un remboursement sans espèces (crédit) reste possible.
5. **Comment on rembourse** : le montant à rendre est calculé par le serveur ; la part payée en **espèces** est rendue du tiroir ; la part **à crédit** diminue la dette du client ; la part payée par **Mobile Money** (Wave, Orange, MTN, Moov) est **rendue par le gérant depuis son téléphone** hors de l'application : l'écran l'affiche (« À rembourser par Wave : 1 500 FCFA ») et le journal garde la mention. Aucun virement automatique dans ce lot.
6. **Montant d'un retour partiel** : par ligne, `arrondi(total de la ligne × quantité rendue ÷ quantité vendue)` (au franc, demi vers le haut), puis la **remise de la vente est répartie au prorata** des lignes. Jamais plus que ce qui a été payé : le cumul des retours d'une ligne ne dépasse pas la quantité vendue.
7. **Remise** (saisie au paiement) : en **FCFA ou en pourcentage**, sur le total ; jusqu'au **seuil du commerce (10 % par défaut)** le caissier la donne seul ; **au-delà**, un gérant la valide avec son PIN (déjà imposé par le serveur). Le ticket écrit la remise sur une ligne. **Sans réseau**, une remise **au-delà du seuil** est refusée (le serveur ne peut pas valider) ; jusqu'au seuil elle passe. La remise ne fait jamais baisser un prix sous zéro : le total net reste supérieur à zéro.
8. **Qui voit quoi** : chaque annulation, retour, remise approuvée ou refusée va au journal (qui, quel ticket, quel motif, qui a validé). Le **rapport du soir** (décision suivante) les comptera par caissier ; ici on les journalise et on les compte dans le résumé de session.
9. **Retrouver le ticket** : un écran **« Ventes du jour »** (réseau obligatoire, ventes des 7 derniers jours, par numéro de ticket ou par heure) permet d'ouvrir une vente et de choisir « Annuler » ou « Retour ». Le caissier voit ses ventes ; le gérant voit tout.
10. **Hors lot** : avoir (bon d'achat) à la place du remboursement, échange (c'est un retour plus une nouvelle vente, faits à la suite), remise **par ligne**, promotions automatiques, retour sans ticket, remboursement Mobile Money automatique, limite de durée d'un retour (tout est journalisé en attendant les règles du pilote).

## Risques et limites

- **La validation par PIN sur la caisse du caissier** : un caissier qui connaît le PIN d'un gérant peut valider seul. Le verrouillage (5 essais, 15 minutes) et le journal limitent le risque, ils ne l'annulent pas. Un PIN de 6 chiffres est conseillé.
- **Mobile Money** : l'application ne prouve pas que le gérant a bien remboursé ; elle garde la trace de ce qui était dû.
- **Retour d'un produit dont le prix a changé** : le remboursement suit le **prix payé** (figé sur la ligne), jamais le prix du catalogue.
- **Marchandise remise en rayon à tort** (article ouvert, périmé) : c'est la décision du gérant, tracée.

## Réexamen

Après le pilote : seuil de remise, durée maximale d'un retour, avoir, remboursement automatique Mobile Money.
