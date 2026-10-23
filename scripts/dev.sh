#!/usr/bin/env bash
# Démarre tout l'environnement de développement de Sôrô : PostgreSQL (Docker), migrations, API et frontend.
# Usage : ./scripts/dev.sh        (Ctrl+C arrête l'API et le frontend ; la base reste démarrée)
#         ./scripts/dev.sh stop   (arrête aussi la base ; les données sont conservées)
#         ./scripts/dev.sh reset  (supprime la base de développement)
set -euo pipefail
export NG_CLI_ANALYTICS=false   # évite que le CLI Angular modifie angular.json

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
STATE="$ROOT/.dev"
DB_CONTAINER=soro-dev
DB_PORT=55432
API_PORT=5140
WEB_PORT=4300   # 4200 est réservé à un autre projet sur cette machine
POSTGRES_IMAGE="${SORO_DEV_POSTGRES_IMAGE:-postgres:16-alpine}"

mkdir -p "$STATE"

case "${1:-start}" in
  stop)  docker stop "$DB_CONTAINER" >/dev/null 2>&1 && echo "Base arrêtée." || echo "Base déjà arrêtée."; exit 0 ;;
  reset) docker rm -f "$DB_CONTAINER" >/dev/null 2>&1 || true; docker volume rm soro-dev-data >/dev/null 2>&1 || true
         echo "Base de développement supprimée."; exit 0 ;;
  start) ;;
  *) echo "Usage : $0 [start|stop|reset]"; exit 1 ;;
esac

for tool in docker dotnet npm openssl; do
  command -v "$tool" >/dev/null || { echo "Outil manquant : $tool"; exit 1; }
done

for port in $API_PORT $WEB_PORT; do
  if (exec 3<>"/dev/tcp/127.0.0.1/$port") 2>/dev/null; then
    echo "Le port $port est déjà utilisé : arrêtez ce qui l'occupe puis relancez."; exit 1
  fi
done

# Secrets de développement : créés une fois, conservés (le poivre doit rester le même pour que les PIN restent valides).
if [ ! -f "$STATE/secrets.env" ]; then
  {
    echo "JWT_KEY=$(openssl rand -base64 48 | tr -d '\n')"
    echo "PIN_PEPPER=$(openssl rand -base64 32 | tr -d '\n')"
  } > "$STATE/secrets.env"
  chmod 600 "$STATE/secrets.env"
fi
# shellcheck disable=SC1091
source "$STATE/secrets.env"

echo "▶ Base PostgreSQL"
if docker ps -a --format '{{.Names}}' | grep -qx "$DB_CONTAINER"; then
  docker start "$DB_CONTAINER" >/dev/null
else
  docker run -d --name "$DB_CONTAINER" -e POSTGRES_PASSWORD=dev -e POSTGRES_DB=soro \
    -v soro-dev-data:/var/lib/postgresql/data -p "127.0.0.1:$DB_PORT:5432" "$POSTGRES_IMAGE" >/dev/null
fi
until docker exec "$DB_CONTAINER" pg_isready -U postgres -d soro >/dev/null 2>&1; do sleep 1; done

echo "▶ Migrations"
dotnet tool list --global | grep -q dotnet-ef || dotnet tool install --global dotnet-ef >/dev/null
export PATH="$PATH:$HOME/.dotnet/tools"
(cd "$ROOT" && SORO_DESIGN_CONNECTION="Host=localhost;Port=$DB_PORT;Database=soro;Username=postgres;Password=dev" \
  dotnet ef database update --project src/backend/Soro.Infrastructure >/dev/null)

echo "▶ Rôle applicatif (sans droits de superutilisateur : l'isolation par commerce s'applique)"
docker exec -i "$DB_CONTAINER" psql -q -U postgres -d soro >/dev/null <<'SQL'
DO $$ BEGIN
  IF NOT EXISTS (SELECT FROM pg_roles WHERE rolname = 'soro_app') THEN
    CREATE ROLE soro_app LOGIN PASSWORD 'app' NOSUPERUSER NOBYPASSRLS;
  END IF;
END $$;
GRANT USAGE ON SCHEMA public TO soro_app;
GRANT SELECT, INSERT, UPDATE ON ALL TABLES IN SCHEMA public TO soro_app;
SQL

PIDS=()
cleanup() {
  echo; echo "Arrêt de l'API et du frontend…"
  # Chaque service tourne dans son propre groupe de processus : on arrête le groupe entier (dotnet/node et leurs enfants).
  for pid in "${PIDS[@]:-}"; do [ -n "$pid" ] && kill -- "-$pid" 2>/dev/null || true; done
  echo "La base reste démarrée (./scripts/dev.sh stop pour l'arrêter)."
}
trap cleanup EXIT INT TERM

export ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS="http://localhost:$API_PORT"
export ConnectionStrings__Soro="Host=localhost;Port=$DB_PORT;Database=soro;Username=soro_app;Password=app"
export Jwt__Issuer=soro-dev Jwt__Audience=soro-dev Jwt__SigningKey="$JWT_KEY" Security__PinPepper="$PIN_PEPPER"
echo "▶ API sur http://localhost:$API_PORT (journal : .dev/api.log)"
setsid bash -c 'cd "$1" && exec dotnet run --no-launch-profile' _ "$ROOT/src/backend/Soro.Api" \
  > "$STATE/api.log" 2>&1 &
PIDS+=($!)

echo "▶ Frontend sur http://localhost:$WEB_PORT (journal : .dev/web.log)"
setsid bash -c 'cd "$1" && { [ -d node_modules ] || npm ci; } && exec npx ng serve --port "$2" --proxy-config proxy.conf.json' _ "$ROOT/src/frontend" "$WEB_PORT" \
  > "$STATE/web.log" 2>&1 &
PIDS+=($!)

until curl -s -o /dev/null "http://localhost:$API_PORT/api/v1/signup/start" 2>/dev/null; do
  kill -0 "${PIDS[0]}" 2>/dev/null || { echo "L'API s'est arrêtée :"; tail -20 "$STATE/api.log"; exit 1; }
  sleep 1
done
until curl -s -o /dev/null "http://localhost:$WEB_PORT" 2>/dev/null; do
  kill -0 "${PIDS[1]}" 2>/dev/null || { echo "Le frontend s'est arrêté :"; tail -20 "$STATE/web.log"; exit 1; }
  sleep 1
done

cat <<MSG

✔ Prêt
  Application : http://localhost:$WEB_PORT
  API         : http://localhost:$API_PORT
  Codes SMS   : visibles dans .dev/api.log  (grep "SMS de développement" .dev/api.log)
Ctrl+C pour arrêter l'API et le frontend.
MSG
wait
