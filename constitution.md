# Constitution du projet Sôrô

## Principes non négociables

1. **Valeur métier avant complexité** : chaque changement répond à un besoin validé du cahier des charges ou à une décision métier documentée.
2. **Clean Architecture** : le domaine ne dépend d'aucune technologie; les dépendances pointent vers le cœur. L'API, la persistance, les fournisseurs et l'interface sont des adaptateurs.
3. **Clean Code** : noms explicites, fonctions cohérentes, responsabilités uniques, erreurs visibles, duplication maîtrisée et abstractions justifiées.
4. **Sécurité et isolation par commerce** : toute lecture ou écriture est autorisée par tenant côté serveur; les contrôles d'accès ne reposent jamais uniquement sur l'interface.
5. **Traçabilité financière** : une vente validée et un mouvement de stock ne sont pas supprimés. Toute correction est une opération inverse, motivée, autorisée et auditée.
6. **Offline-first résilient** : les opérations confirmées sont persistées localement avant accusé de réception; les identifiants créés sur appareil permettent une synchronisation idempotente.
7. **UX adaptée au terrain** : français clair, FCFA sans décimales, grands contrôles tactiles, retours immédiats et parcours utilisable sur matériel modeste et connexion intermittente.
8. **Accessibilité et responsive** : contraste lisible, navigation clavier, états explicites, cibles tactiles confortables et mise en page mobile sans débordement.
9. **Qualité vérifiable** : chaque exigence livrée possède des critères d'acceptation observables et des validations adaptées à son risque.
10. **Décisions explicites** : hypothèses, limites fournisseurs, exigences légales, arbitrages UX et choix techniques sont documentés; les inconnues métier restent ouvertes.

## Architecture cible

- Backend : .NET 10 LTS, ASP.NET Core, PostgreSQL 18, EF Core 10/Npgsql selon le cahier des charges.
- Frontend : Angular 22 PWA; IndexedDB pour le journal local et la file de synchronisation; style avec Tailwind CSS (pas de Bootstrap), les jetons de `DESIGN.md` vivant dans le thème Tailwind (voir `docs/decisions/0001-stack-et-tailwind.md`).
- Couches backend : Domain → Application → Infrastructure; API comme couche de présentation. Domain et Application ne référencent pas Infrastructure ni API.
- Modules métier initiaux : Tenancy/Identity, Point de vente, Catalogue/Stock, Clients/Crédit, Reporting, Abonnement. Éviter le découpage prématuré en microservices.
- Toute interface externe (WhatsApp, SMS, Mobile Money, impression) est encapsulée derrière un contrat d'application et un adaptateur remplaçable.

## Flux Spec Kit

Pour chaque capacité, maintenir une spécification avec scénarios testables, un plan technique, des tâches traçables et une revue des écarts. Aucun choix métier non validé ne devient une contrainte définitive. Les décisions globales sont consignées dans `docs/decisions/`.

## Gate UX/UI

Avant implémentation d'un écran : parcours et états (chargement, vide, erreur, hors ligne, conflit), règles responsive, composants et contenu sont décrits dans `DESIGN.md` et la spécification concernée. Les maquettes sont validées sur les largeurs mobiles et desktop représentatives avant de figer les détails visuels.

## Dérogations

Toute dérogation à la constitution doit inclure sa motivation, son périmètre, ses risques et sa date de réexamen dans une décision d'architecture.
