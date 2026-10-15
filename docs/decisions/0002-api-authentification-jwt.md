# 0002 — API HTTP : authentification par jeton JWT et conventions

**Statut :** accepté.

## Décision

- L'API (`Soro.Api`, ASP.NET Core, endpoints minimaux) est versionnée dans l'URL : `/api/v1/...`.
- Elle **valide** des jetons JWT signés en HMAC-SHA256 (émetteur, public, expiration, signature). Le jeton porte
  `sub` (utilisateur), `tenant_id` (commerce) et `role` (`Owner`, `Manager`, `Cashier`, `StockKeeper`, `Accountant`).
  Un jeton valide sans ces trois revendications est refusé (401).
- Le commerce et l'utilisateur viennent **toujours** du jeton, jamais du corps ni de l'URL (Constitution 4).
- L'**émission** des jetons (connexion par PIN, mot de passe et code SMS du propriétaire, révocation d'appareil)
  n'est pas dans cette API : elle appartient au module Identité, à construire (tâche T027).
- Erreurs au format `application/problem+json` avec une extension `code` stable (`not_found` 404, `forbidden` 403,
  `rule_violation` 422, `mobile_money_reference_used` et `register_already_open` 409, `concurrent_conflict` 409).
  Aucune trace de pile, de SQL ou de chemin n'est renvoyée.
- Limitation de débit globale : 600 requêtes par minute et par utilisateur (adresse pour les non authentifiés),
  réglable par `RateLimit:PermitsPerMinute`.
- Une écriture concurrente de la même clé renvoie 409 `concurrent_conflict` : le client renvoie sa requête, qui est
  alors acquittée comme déjà traitée (cas d'usage idempotents).

## Configuration (aucun secret dans le dépôt)

| Clé | Rôle |
|---|---|
| `ConnectionStrings:Soro` | Connexion PostgreSQL du **rôle applicatif** (ni superutilisateur ni `BYPASSRLS`) |
| `Jwt:Issuer`, `Jwt:Audience` | Doivent correspondre à l'émetteur de jetons |
| `Jwt:SigningKey` | Clé HMAC en Base64, 32 octets au minimum |

En développement : `dotnet user-secrets`. En production : variables d'environnement (`Jwt__SigningKey`, …) ou
gestionnaire de secrets. Le démarrage échoue si une clé manque ou si la clé de signature est trop courte.

## Conséquences et limites connues

- Tant que le module Identité n'existe pas, aucun client ne peut obtenir de jeton hors des tests.
- `IUserDirectory` est provisoire (`UnavailableUserDirectory`) : un autre utilisateur ne peut pas valider une remise
  par son identifiant ; seul un gérant connecté peut accorder lui-même une remise au-delà du seuil.
- La synchronisation d'un lot partage un contexte de base : si une écriture concurrente échoue, tout le lot reçoit 409
  et le client le renvoie en entier (les ventes déjà enregistrées sont acquittées). À affiner si cela gêne en pratique.
- Pas de CORS ni de HTTPS forcé dans l'API : à décider avec l'hébergement (proxy inverse, origine de la PWA).
- Le rôle PostgreSQL applicatif est créé par les tests uniquement : un script d'exploitation reste à écrire.

## Réexamen

À la livraison du module Identité, ou si l'émission de jetons doit être confiée à un fournisseur externe.
