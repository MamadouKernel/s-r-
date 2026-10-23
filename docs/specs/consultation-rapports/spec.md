# Spécification : Consultation et rapports (V1)

**Statut :** brouillon dérivé du cahier des charges (promesse « le propriétaire voit tout en direct ») ; à valider avec le pilote.

## Objectif

Permettre au propriétaire, au gérant et au comptable de consulter les ventes, les sessions de caisse avec leur écart,
et un rapport d'activité sur une période, sans rien modifier (REQ-08 : écart visible du gérant/propriétaire).

## Principes

- Lecture seule : aucune écriture, aucun audit.
- Côté lecture, pas de rechargement des agrégats du domaine : des requêtes dédiées projettent directement des vues.
- Isolation par commerce comme partout (contrôle applicatif + RLS). Les périodes sont des intervalles `[from, to[` en UTC.

## Exigences

### REP-01 Liste des ventes
`GET /api/v1/sales` : filtres `from`, `to`, `sessionId`, `cashierId`, `includeCancelled` (défaut : oui), pagination `limit`
(défaut 50, max 200) et `offset`. Tri du plus récent au plus ancien. Chaque ligne : identifiant, date, caissier, caisse,
session, total net, remise, statut, moyens de paiement utilisés, nombre de lignes.

### REP-02 Détail d'une vente
`GET /api/v1/sales/{id}` : lignes (nom et prix figés), paiements (moyen, montant, monnaie rendue, référence Mobile Money),
annulation (qui, quand, motif).

### REP-03 Sessions de caisse
`GET /api/v1/register-sessions` : filtres `status`, `from`, `to` (sur l'ouverture), `onlyWithVariance`. Chaque ligne : caissier,
caisse, ouverture, fond, espèces encaissées, montant attendu, montant compté, écart, clôture.

### REP-04 Rapport d'activité
`GET /api/v1/reports/activity?from=&to=` : nombre et total des ventes validées, remises, ventes annulées (nombre, total),
répartition par moyen de paiement (montant, nombre), répartition par caissier (nombre, total), sessions clôturées et
cumul des écarts (manques et excédents séparés).

## Droits

Propriétaire, gérant et comptable (lecture). Le caissier et le magasinier reçoivent 403.

## Règles

- `from` doit précéder `to` ; l'intervalle ne dépasse pas 366 jours ; sinon 422.
- Une vente annulée ne compte pas dans le chiffre d'affaires mais figure dans les annulations.
- Le total par moyen de paiement ignore la monnaie rendue (montant dû, pas montant remis).

## Scénarios d'acceptation

- **SC-R1** Un caissier demande la liste des ventes : 403.
- **SC-R2** Un autre commerce ne voit aucune vente ni session du premier.
- **SC-R3** Trois ventes (deux espèces, une Orange Money) dont une annulée : le rapport donne 2 ventes validées, 1 annulée,
  la bonne répartition par moyen de paiement.
- **SC-R4** Une session clôturée avec un manque de 500 FCFA apparaît avec `onlyWithVariance` et son écart de -500.
- **SC-R5** Une période inversée ou de plus de 366 jours est refusée.

## Questions ouvertes

- Périmètre par point de vente pour un gérant (le modèle de point de vente n'est pas encore porté par l'utilisateur).
- Fuseau horaire du commerce pour « la journée » : le client envoie des bornes UTC en attendant.
- Export (CSV, PDF, WhatsApp) : hors de ce lot.
