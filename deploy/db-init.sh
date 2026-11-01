#!/bin/sh
# Prépare les rôles de la base Sôrô (docs/specs/exploitation EXP-06). À lancer une fois, avec un compte administrateur de la base,
# avant la première migration : dans docker-entrypoint-initdb.d pour les essais, à la main (psql) sur un serveur réel.
#
#   soro_owner : propriétaire de la structure ; applique les migrations. Jamais utilisé par l'API. Il contourne l'isolation par commerce,
#                car une migration qui reprend des données (ex. produits en familles) doit voir les lignes de tous les commerces.
#   soro_app   : celui de l'API. Pas de superutilisateur, pas de contournement de l'isolation par commerce, pas de suppression.
#   soro_ops   : l'outil d'exploitation : voit tous les commerces (rappels, rapport du soir, paiements d'abonnement).
#
# Variables : SORO_OWNER_PASSWORD, SORO_APP_PASSWORD, SORO_OPS_PASSWORD (obligatoires) ; PGUSER, PGPASSWORD, PGHOST et PGDATABASE (ou POSTGRES_DB) pour se connecter.
set -eu
: "${SORO_OWNER_PASSWORD:?SORO_OWNER_PASSWORD est obligatoire}"
: "${SORO_APP_PASSWORD:?SORO_APP_PASSWORD est obligatoire}"
: "${SORO_OPS_PASSWORD:?SORO_OPS_PASSWORD est obligatoire}"

# La base de Sôrô : PGDATABASE, ou POSTGRES_DB (fournie par l'image PostgreSQL pendant l'initialisation).
DATABASE="${PGDATABASE:-${POSTGRES_DB:?PGDATABASE (ou POSTGRES_DB) est obligatoire : la base de Sôrô}}"

psql -d "$DATABASE" -v ON_ERROR_STOP=1 -q \
  -v owner_pw="$SORO_OWNER_PASSWORD" -v app_pw="$SORO_APP_PASSWORD" -v ops_pw="$SORO_OPS_PASSWORD" <<'SQL'
SELECT format('CREATE ROLE soro_owner LOGIN PASSWORD %L NOSUPERUSER BYPASSRLS', :'owner_pw')
  WHERE NOT EXISTS (SELECT FROM pg_roles WHERE rolname = 'soro_owner') \gexec
SELECT format('CREATE ROLE soro_app LOGIN PASSWORD %L NOSUPERUSER NOBYPASSRLS', :'app_pw')
  WHERE NOT EXISTS (SELECT FROM pg_roles WHERE rolname = 'soro_app') \gexec
SELECT format('CREATE ROLE soro_ops LOGIN PASSWORD %L NOSUPERUSER BYPASSRLS', :'ops_pw')
  WHERE NOT EXISTS (SELECT FROM pg_roles WHERE rolname = 'soro_ops') \gexec

SELECT format('ALTER DATABASE %I OWNER TO soro_owner', current_database()) \gexec
ALTER SCHEMA public OWNER TO soro_owner;
GRANT USAGE ON SCHEMA public TO soro_app, soro_ops;
-- Tout ce que soro_owner créera (les tables des migrations) est lisible et écrivable par l'API et l'outil, jamais supprimable.
ALTER DEFAULT PRIVILEGES FOR ROLE soro_owner IN SCHEMA public GRANT SELECT, INSERT, UPDATE ON TABLES TO soro_app, soro_ops;
SQL
