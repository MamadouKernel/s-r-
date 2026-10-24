# Spécification — Clients et crédit

**Statut :** brouillon ; décisions prises avec le porteur du projet le 24/10/2026 (voir `docs/decisions/0008-clients-et-credit.md`). Règles de l'analyse : RG-06 (plafond), RG-20 (somme des paiements = total, crédit inclus), domaine D4, transaction T9.
**Lots :** (1) serveur : clients, dette, plafond, versements, vente à crédit ; (2) écrans : fiche client, crédit à la caisse, versement, mode hors ligne.

## Objectif

Un commerçant vend souvent « à crédit » : il note la dette sur un cahier. Sôrô doit savoir **qui doit combien**, **empêcher qu'un client s'endette sans limite**, enregistrer les **versements** (acomptes compris) et rester utilisable sans réseau.

## Exigences

### CRD-01 Fiche client
Un client a un **nom** (2 à 100 caractères), un **téléphone ivoirien facultatif** (10 chiffres, normalisé en `+225XXXXXXXXXX` ; le même numéro ne peut pas servir deux clients du commerce), un **plafond de crédit** (FCFA, ≥ 0) et une **dette** (FCFA, ≥ 0, calculée par le serveur).
Création : propriétaire, gérant et caissier (un client se crée à la caisse) ; le caissier ne choisit pas le plafond, le client reçoit celui par défaut du commerce. Modification du nom, du téléphone et du plafond : propriétaire et gérant. Un client ne se supprime jamais : on le **désactive** (il disparaît des listes de la caisse, sa dette et son historique restent).
Un client avec une dette ne peut pas être désactivé.

### CRD-02 Plafond par défaut
Le plafond d'un nouveau client est le réglage du commerce (`DefaultCreditCeilingFcfa`, **20 000 FCFA** tant que les paramètres du commerce ne sont pas stockés, T028). Le propriétaire et le gérant le changent client par client.

### CRD-03 Lecture
Liste des clients actifs avec nom, téléphone, plafond, dette et **crédit disponible** (plafond − dette, jamais négatif) ; recherche par nom ou par chiffres du téléphone ; clients désactivés sur demande. Lecture : propriétaire, gérant, caissier, comptable (qui voit les dettes).
Historique des versements d'un client : propriétaire, gérant, comptable.

### CRD-04 Vente à crédit (RG-06, RG-20)
Une vente avec un paiement « Crédit » exige un client **actif du commerce**. Le montant à crédit s'ajoute à sa dette dans la même transaction que la vente.
Si `dette + crédit > plafond` : **refus (403)** avec le plafond, la dette et ce qui reste, sauf si un **gérant** (ou le propriétaire) valide : soit c'est lui qui encaisse, soit il est désigné comme validateur (`creditApproverId`, même mécanisme que la remise). La validation est tracée (`credit.limit.approved`), le refus aussi (`credit.limit.refused`).
**Hors ligne** (ventes envoyées par la file de synchronisation) : la vente a déjà eu lieu ; le serveur l'**enregistre toujours**. Si le plafond est dépassé sans validation (la dette a changé entre-temps, sur une autre caisse), il le **signale** dans le journal (`credit.limit.exceeded`, avec le client, la dette et le plafond) pour que le gérant agisse.
Une vente à crédit pour un client inconnu, d'un autre commerce ou désactivé est rejetée avec un message clair.

### CRD-05 Versement (remboursement de dette)
`POST /api/v1/customers/{id}/repayments` : identifiant de versement (rejouable), montant (> 0 et **au plus la dette**), moyen (espèces ou Mobile Money, jamais crédit), référence de transaction obligatoire pour le Mobile Money (mêmes règles anti-doublon que les ventes : une référence ne sert qu'une fois par commerce et par opérateur, ventes et versements confondus). Le versement baisse la dette et est tracé dans le journal (`debt.repaid`) avec qui, quand, combien, moyen.
Propriétaire, gérant et caissier. **En ligne seulement** (le serveur est la source de la dette).

### CRD-06 Annulation d'une vente à crédit
Annuler une vente à crédit (gérant, RG-02) **annule la dette correspondante** (la part payée à crédit). Le reste (espèces rendues, stock) dépend de T017 et n'est pas changé ici.

### CRD-07 Cohérence
La dette d'un client est toujours égale à la somme des paiements « Crédit » de ses ventes **validées** moins la somme de ses versements. Deux ventes à crédit simultanées pour un même client ne perdent aucune écriture (contrôle de concurrence : la seconde est rejouée).

### CRD-08 Droits et isolation
Un autre commerce ne voit ni les clients, ni les dettes, ni les versements ; un identifiant de client d'un autre commerce est introuvable. Un caissier ne peut ni changer un plafond, ni désactiver un client, ni lire l'historique des versements.

## Scénarios d'acceptation

- **SC-C1** Créer un client avec nom et téléphone : plafond par défaut, dette 0 ; le même téléphone (autre écriture) est refusé (409).
- **SC-C2** Vendre à crédit 8 000 à un client de plafond 20 000 : sa dette passe à 8 000, le crédit disponible à 12 000.
- **SC-C3** Une vente de 15 000 à crédit au même client (dette 8 000, plafond 20 000) est refusée (403) avec « il reste 12 000 FCFA de crédit » ; avec la validation d'un gérant, elle passe et la dette est 23 000.
- **SC-C4** Une vente envoyée par la synchronisation qui dépasse le plafond est enregistrée et signalée dans le journal.
- **SC-C5** Un versement partiel de 5 000 baisse la dette ; un versement supérieur à la dette est refusé ; un rejeu du même versement ne le compte pas deux fois.
- **SC-C6** Un versement Mobile Money sans référence est refusé ; une référence déjà utilisée par une vente ou un versement l'est aussi.
- **SC-C7** Annuler une vente à crédit réduit la dette de son montant à crédit.
- **SC-C8** Deux ventes à crédit simultanées pour un même client : la dette totale est exacte.
- **SC-C9** Un client avec une dette ne peut pas être désactivé ; sans dette, il disparaît des listes mais ses ventes restent lisibles.
- **SC-C10** Un caissier crée un client mais ne change pas son plafond ; un autre commerce ne voit rien.

## Hors lot

Écrans et mode hors ligne (lot 2), relances (SMS/WhatsApp exclus ; autre canal à choisir), échéancier, intérêts, points de fidélité, regroupement de clients, fusion de doublons, export, crédit fournisseur.
