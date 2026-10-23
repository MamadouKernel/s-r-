# Tâches : Caisse MVP hors ligne

Dérivé de `spec.md` et `plan.md`. Format : `[ID] [P?] Description — exigences/scénarios`. `[P]` = parallélisable (fichiers distincts). Chaque tâche de code est précédée de ses tests (RED → GREEN → REFACTOR).

## Phase 0 — Bootstrap

- [x] T001 Solution `Soro.slnx`, `Directory.Build.props` (net10.0, nullable, avertissements en erreurs), `.editorconfig`, `.gitignore`
- [x] T002 Projet `Soro.Domain` sans aucune dépendance externe
- [x] T003 [P] Projet `Soro.Domain.Tests` (xunit)
- [x] T004 Test d'architecture : le domaine ne référence aucun assembly hors BCL — Constitution 2

## Phase 1 — Domaine (étape 1 du plan)

- [x] T010 Objet-valeur `Money` : FCFA entier, non négatif, arithmétique sûre — RG-18
- [x] T011 [P] Identifiants typés (`TenantId`, `SaleId`, `SessionId`…) générables hors ligne (UUID) — RG-17
- [x] T012 `CashRegisterSession` : ouverture avec fond, refus de vente si fermée, clôture avec montant attendu et écart — REQ-01, REQ-08, RG-01, RG-24
- [x] T013 `Payment` : espèces avec monnaie rendue, Mobile Money avec référence obligatoire, crédit avec client — REQ-03, REQ-04, RG-04
- [x] T014 `SaleLine` : quantité strictement positive, prix unitaire figé — RG-21
- [x] T015 `Sale` : totaux cohérents, paiements = total net, attribution complète, annulation motivée par opération inverse — REQ-07, RG-02, RG-20
- [x] T016 `CashRegisterSession.RecordSale(SaleDraft)` : seule porte de création d'une vente, rattachée à la session ouverte, cumul des espèces encaissées — SC-01
- [ ] T017 Effet d'une annulation sur la caisse (remboursement espèces, écriture inverse du stock) : règle de remboursement à valider avec le pilote — RG-02

## Phase 2 — Application et API (à planifier)

- [x] T020 Projet `Soro.Application` et ports (dépôts, horloge, tenant et utilisateur courants)
- [x] T021 Cas d'usage `OpenRegisterSession`
- [x] T022 Cas d'usage `RecordSale` avec contrôle des rôles (remise, annulation) — SC-04
- [x] T023 Cas d'usage `SyncSales` idempotent par UUID — REQ-06, SC-03
- [x] T024 Cas d'usage `CloseRegisterSession`
- [x] T025 Projet `Soro.Infrastructure` : EF Core 10/Npgsql, unicités, RLS — SC-05, SC-06
- [x] T026 Projet `Soro.Api` : endpoints versionnés, authentification, erreurs de synchronisation structurées

- [x] T027 Module Identité : annuaire des utilisateurs, connexion par PIN, émission des jetons, équipe — `docs/decisions/0003-identite-pin-et-jetons.md` (reste : mot de passe + SMS du propriétaire, enrôlement d'appareil, premier propriétaire)
- [ ] T028 Script d'exploitation du rôle PostgreSQL applicatif et paramètres du commerce stockés (seuils, plafonds)

## Phase 3 — PWA (à planifier après validation du gate UX/UI)

- [ ] T030 Maquettes des parcours de `DESIGN.md` §8, validées mobile et desktop
- [x] T033 Projet Angular PWA dans `src/frontend/` avec Tailwind CSS et thème issu de `DESIGN.md` — `docs/decisions/0001-stack-et-tailwind.md`
- [x] T031 Journal IndexedDB et outbox ordonnée par caisse — REQ-05, SC-02 (`docs/specs/caisse-pwa/spec.md`, ADR 0004)
- [x] T032 Écrans de caisse : session, vente, paiement, clôture (structure et états faits ; détail visuel à valider avec T030)
- [x] T034 Panier durable et vente en attente, base locale v2 avec migration — `docs/specs/caisse-pwa/spec.md` PWA-10

## Catalogue et stock (V1)

Voir `docs/specs/catalogue-stock/spec.md`.

- [x] T040 Domaine `Product` et `StockMovement` (append-only) — CAT-01, CAT-02, STK-03
- [x] T041 Cas d'usage créer/modifier/désactiver produit, consulter, alertes — CAT-01 à CAT-03, STK-05
- [x] T042 Cas d'usage réception et perte — STK-01, STK-02
- [x] T043 Persistance EF Core, migration, RLS, mouvements immuables — SC-C2, SC-C6
- [x] T044 Endpoints API catalogue et stock
- [x] T045 Décrément du stock à la vente — STK-04

## Gestion des produits et du stock — écrans (V1)

Voir `docs/specs/gestion-produits-stock/spec.md`.

- [x] T060 Service API de gestion et garde par profil — GES-01 à GES-05
- [x] T061 Écrans liste, fiche produit, réception, perte, historique — GES-01 à GES-06 (auteur affiché par identifiant en attendant l'écran Équipe)

## Équipe — lecture serveur et écran (V1)

Voir `docs/specs/equipe/spec.md`.

- [x] T070 Lecture de l'équipe et annuaire côté serveur — EQ-01, EQ-02
- [x] T071 Écran Équipe : liste, ajout, désactivation — EQ-03 à EQ-05
- [x] T072 Auteurs nommés dans l'historique du stock — EQ-06

## Tableau de bord du propriétaire (V1)

Voir `docs/specs/tableau-de-bord/spec.md`.

- [x] T080 Périodes, indicateurs et service de lecture — TDB-01 à TDB-05
- [x] T081 Page, droits, navigation, actualisation — TDB-06, TDB-07, SC-D5, SC-D8

## Meilleures ventes et journal des actions sensibles (V1)

Voir `docs/specs/meilleures-ventes-journal/spec.md`.

- [x] T090 Classement des produits côté serveur — MV-01
- [x] T091 Journal des actions sensibles et annuaire du comptable côté serveur — MV-02, MV-03
- [x] T092 Bloc « Meilleures ventes » du tableau de bord — MV-04
- [x] T093 Page Journal — MV-05

## Consultation et rapports (V1)

Voir `docs/specs/consultation-rapports/spec.md`.

- [x] T050 Ports et vues de lecture, droits de consultation — REP-01 à REP-04
- [x] T051 Cas d'usage de lecture (validation des périodes, pagination) — SC-R1, SC-R5
- [x] T052 Requêtes EF Core en lecture seule — SC-R2, SC-R3, SC-R4
- [x] T053 Endpoints API et tests d'intégration

## Points bloquants non tranchés

Voir `spec.md` (questions ouvertes) et `docs/analyse-merise.md` §11. Le domaine de la phase 1 n'impose aucun de ces choix : numérotation de ticket (OP-1), validité du Mobile Money hors ligne (OP-3) et politique de conflit de stock (OP-4) restent hors du modèle.
