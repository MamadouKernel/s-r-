# Architecture cible

## Vue d'ensemble

Sôrô est un SaaS multi-commerce avec une PWA de caisse capable d'enregistrer des ventes localement. Le backend centralise les règles et les données synchronisées. Chaque tenant est isolé par contrôle applicatif et politiques PostgreSQL RLS.

```text
Angular PWA (caisse / gestion / propriétaire)
  ├─ Application UI et parcours
  ├─ IndexedDB : catalogue disponible, sessions, opérations en attente
  └─ Sync client : outbox idempotente, reprise et résolution explicite des erreurs
             │ HTTPS API / SignalR
ASP.NET Core API
  ├─ Domain : entités, objets-valeurs, invariants, événements métier
  ├─ Application : cas d'usage, ports, autorisation, orchestration
  ├─ Infrastructure : EF Core, PostgreSQL, adaptateurs externes, jobs
  └─ Presentation : endpoints, auth, validation HTTP, contrats API
             │
PostgreSQL 18 (tenant_id, contraintes, RLS, audit, sauvegardes)
```

## Règles de dépendance

- Le domaine ne dépend ni d'EF Core, ni d'ASP.NET, ni d'un fournisseur.
- L'Application dépend du Domain et expose des ports pour persistance, temps, notifications et autres effets externes.
- Infrastructure implémente les ports; Presentation traduit les requêtes HTTP vers les cas d'usage.
- Les DTO HTTP ne sont pas les entités du domaine ni les modèles persistés.
- Les règles critiques sont appliquées côté serveur; le client hors ligne applique seulement les règles nécessaires à la continuité, puis le serveur valide à la synchronisation.

## Caisse hors ligne et synchronisation

Chaque opération confirmée reçoit un UUID généré à la caisse, un tenant, un point de vente, une caisse, un utilisateur et une séquence locale. La PWA persiste l'opération dans IndexedDB avant de la présenter comme enregistrée. L'API accepte des commandes idempotentes et conserve les identifiants traités. La reprise réseau renvoie les commandes dans l'ordre de la caisse; les accusés de réception sont persistés localement. Erreurs permanentes et conflits sont visibles et actionnables, jamais supprimés silencieusement.

La numérotation de tickets sans trou est en tension avec la vente hors ligne multi-appareil : réserver des numéros cloud à l'avance ou garantir « sans trou » n'est pas encore décidé. À trancher avant implémentation de la numérotation réglementaire.

## Multi-tenancy et données

- Toute ligne métier porte `TenantId`; accès contrôlé à partir du contexte authentifié, jamais depuis une valeur client seule.
- RLS PostgreSQL est une seconde barrière, testée avec le rôle applicatif réel et après réinitialisation de contexte de connexion.
- Index et contraintes uniques incluent le tenant lorsque nécessaire; les clés hors ligne sont uniques globalement.
- Les opérations financières et mouvements de stock sont append-only; corrections via écritures compensatoires.
- Sauvegardes chiffrées, restauration exercée, exports tenant et politique de conservation à préciser.

## Découpage de solution proposé

```text
src/backend/Soro.Domain
src/backend/Soro.Application
src/backend/Soro.Infrastructure
src/backend/Soro.Api
src/frontend/ (Angular PWA)
tests/backend/{Domain,Application,Integration,Architecture}
```

Les noms de projets et conventions de code seront confirmés au bootstrap du dépôt. Les tests restent organisés autour des invariants et contrats, pas autour des détails privés des classes.

## Décisions restant à valider

- Sémantique et autorité de validation des paiements Mobile Money saisis manuellement hors ligne.
- Politique de conflits si un prix, un crédit ou un stock change pendant une longue période hors ligne.
- Numérotation des tickets/factures hors ligne et règles fiscales applicables.
- Authentification hors ligne/PIN, révocation d'appareil et durée d'accès local.
- Architecture d'impression navigateur versus agent local .NET selon le matériel du pilote.
- Fournisseurs WhatsApp/SMS/paiement, hébergement, objectifs RPO/RTO et politique de conservation.
