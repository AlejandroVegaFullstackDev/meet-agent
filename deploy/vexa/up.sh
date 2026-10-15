#!/usr/bin/env bash
# Levanta Vexa Lite (bot de Meet, Apache-2.0) de forma autohospedada y SIN depender de vexa.ai:
#  - la transcripción la hace nuestro meetagent-stt (Whisper local) en su propio contenedor;
#  - secretos aleatorios en deploy/vexa/.env.local (no se versiona);
#  - el gateway solo escucha en 127.0.0.1 (no queda expuesto a la red);
#  - a diferencia de `make lite`, NO monta ~/.claude/.credentials.json ni ninguna credencial del usuario.
# Uso: deploy/vexa/up.sh [STT_MODEL=small-q5_1]  ·  Parar: deploy/vexa/down.sh
# Modelo por defecto: base-q5_1. El bot de Vexa corta cada petición a los 30 s y en este portátil
# small tardaba ~19 s por fragmento con la CPU cargada (base: ~2,5 s).
set -euo pipefail
cd "$(dirname "$0")/../.."
HERE=deploy/vexa
ENV_LOCAL=$HERE/.env.local
NET=meetagent-vexa
PG=meetagent-vexa-postgres STORAGE=meetagent-vexa-storage APP=meetagent-vexa STT=meetagent-vexa-stt
PG_IMAGE=postgres:17-alpine
STORAGE_IMAGE=versity/versitygw:v1.8.0@sha256:30292fc2eeacc67a36993b01f7a7a5e3361a19cced0e80c1d71cfa2a4b0a2499
APP_IMAGE=${VEXA_IMAGE:-vexaai/vexa-lite:v012}
GATEWAY_PORT=${VEXA_PORT:-8056}
STT_PORT=${STT_PORT:-8083}
DOTNET="$HOME/.dotnet/dotnet"; [ -x "$DOTNET" ] || DOTNET=dotnet

rnd() { head -c 24 /dev/urandom | base64 | tr -dc 'A-Za-z0-9' | head -c 32; }
if [ ! -f "$ENV_LOCAL" ]; then
  umask 077
  cat > "$ENV_LOCAL" <<ENV
ADMIN_TOKEN=$(rnd)
PG_PASSWORD=$(rnd)
STORAGE_ACCESS_KEY=$(rnd)
STORAGE_SECRET_KEY=$(rnd)
STT_TOKEN=$(rnd)
ENV
  echo "Secretos nuevos en $ENV_LOCAL"
fi
set -a; . "$ENV_LOCAL"; set +a

docker network inspect "$NET" >/dev/null 2>&1 || docker network create "$NET" >/dev/null

echo "1/5 STT local (meetagent-stt en contenedor, solo dentro de la red $NET)"
# En contenedor y no en el host: con ufw activo, los contenedores no alcanzan puertos del host,
# y así no hay que tocar el firewall. El puerto no se publica fuera de Docker.
if ! docker ps -q -f "name=^$STT$" | grep -q .; then
  build=$(mktemp -d)
  "$DOTNET" publish app/src/MeetAgent.SttServer -c Release -o "$build" --nologo -v quiet -p:DebugType=none
  docker build -q -t meetagent-stt:local -f "$HERE/stt.Dockerfile" "$build" >/dev/null
  rm -rf "$build"
  docker rm -f "$STT" >/dev/null 2>&1 || true
  docker run -d --name "$STT" --network "$NET" --restart unless-stopped \
    -e MEETAGENT_STT_TOKEN="$STT_TOKEN" -v "$PWD/models:/meetagent/models:ro" \
    meetagent-stt:local --model "${STT_MODEL:-base-q5_1}" >/dev/null
fi
for _ in $(seq 1 40); do docker exec "$STT" sh -c 'exit 0' 2>/dev/null && \
  docker run --rm --network "$NET" "$PG_IMAGE" wget -q -O /dev/null "http://$STT:$STT_PORT/health" 2>/dev/null && break; sleep 1; done
TX_URL="http://$STT:$STT_PORT"

echo "2/5 PostgreSQL"
if ! docker ps -q -f "name=^$PG$" | grep -q .; then
  docker rm -f "$PG" >/dev/null 2>&1 || true
  docker run -d --name "$PG" --network "$NET" --restart unless-stopped \
    -e POSTGRES_DB=vexa -e POSTGRES_USER=postgres -e POSTGRES_PASSWORD="$PG_PASSWORD" -e TZ=UTC -e PGTZ=UTC \
    -v meetagent-vexa-pgdata:/var/lib/postgresql/data "$PG_IMAGE" -c idle_in_transaction_session_timeout=60000 >/dev/null
fi
for _ in $(seq 1 30); do docker exec "$PG" pg_isready -U postgres -d vexa -q 2>/dev/null && break; sleep 2; done

echo "3/5 Almacenamiento S3 local (versitygw)"
if ! docker ps -q -f "name=^$STORAGE$" | grep -q .; then
  docker rm -f "$STORAGE" >/dev/null 2>&1 || true
  docker run -d --name "$STORAGE" --network "$NET" --restart unless-stopped \
    -e ROOT_ACCESS_KEY="$STORAGE_ACCESS_KEY" -e ROOT_SECRET_KEY="$STORAGE_SECRET_KEY" \
    -v meetagent-vexa-storage:/srv/vexa-storage --entrypoint /bin/sh "$STORAGE_IMAGE" -c \
    'mkdir -p /srv/vexa-storage/data /srv/vexa-storage/iam && exec versitygw --port :9000 --health /health --iam-dir /srv/vexa-storage/iam --quiet posix /srv/vexa-storage/data' >/dev/null
fi
for _ in $(seq 1 30); do docker exec "$STORAGE" wget -q -O /dev/null http://127.0.0.1:9000/health 2>/dev/null && break; sleep 1; done
out=$(docker run --rm --network "$NET" -e ADMIN_ACCESS_KEY="$STORAGE_ACCESS_KEY" -e ADMIN_SECRET_KEY="$STORAGE_SECRET_KEY" \
  "$STORAGE_IMAGE" admin --endpoint-url "http://$STORAGE:9000" create-bucket --bucket vexa --owner "$STORAGE_ACCESS_KEY" 2>&1) \
  || echo "$out" | grep -q BucketAlreadyOwnedByYou || { echo "✗ bucket: $out"; exit 1; }

echo "4/5 Vexa Lite ($APP_IMAGE) → http://127.0.0.1:$GATEWAY_PORT"
docker rm -f "$APP" >/dev/null 2>&1 || true
docker run -d --name "$APP" --shm-size=2g --network "$NET" --restart unless-stopped \
  -p "127.0.0.1:$GATEWAY_PORT:8056" \
  -e IMAGE_TAG=v012 \
  -e DB_HOST="$PG" -e DB_PORT=5432 -e DB_NAME=vexa -e DB_USER=postgres -e DB_PASSWORD="$PG_PASSWORD" \
  -e ADMIN_API_TOKEN="$ADMIN_TOKEN" -e ADMIN_TOKEN="$ADMIN_TOKEN" \
  -e TRANSCRIPTION_SERVICE_URL="$TX_URL" -e TRANSCRIPTION_SERVICE_TOKEN="$STT_TOKEN" \
  -e MINIO_ENDPOINT="$STORAGE:9000" -e MINIO_ACCESS_KEY="$STORAGE_ACCESS_KEY" -e MINIO_SECRET_KEY="$STORAGE_SECRET_KEY" \
  -e MINIO_BUCKET=vexa -e MINIO_SECURE=false \
  -e VEXA_PUBLIC_API_URL="http://localhost:$GATEWAY_PORT" \
  "$APP_IMAGE" >/dev/null
for _ in $(seq 1 60); do curl -sf -o /dev/null "http://127.0.0.1:$GATEWAY_PORT/health" && break; sleep 5; done
curl -sf -o /dev/null "http://127.0.0.1:$GATEWAY_PORT/health" || { echo "✗ el gateway no respondió (docker logs $APP)"; exit 1; }

echo "5/5 API key de MeetAgent"
if ! grep -q '^VEXA_API_KEY=' "$ENV_LOCAL"; then
  key=$(docker exec -i -e ADMIN="$ADMIN_TOKEN" "$APP" python3 - <<'PY'
import os, json, urllib.request, urllib.error
B = "http://localhost:8001"; H = {"X-Admin-API-Key": os.environ["ADMIN"], "Content-Type": "application/json"}
def call(m, p, body=None):
    req = urllib.request.Request(B + p, data=json.dumps(body).encode() if body is not None else None, method=m, headers=H)
    try:
        with urllib.request.urlopen(req, timeout=10) as r: return r.status, r.read().decode()
    except urllib.error.HTTPError as e: return e.code, e.read().decode()
    except Exception: return 0, ""
import time
for _ in range(90):   # admin-api tarda en arrancar dentro del contenedor
    s, b = call("GET", "/admin/users/email/meetagent@localhost")
    if s in (200, 404): break
    time.sleep(2)
uid = json.loads(b)["id"] if s == 200 else json.loads(call("POST", "/admin/users", {"email": "meetagent@localhost", "name": "MeetAgent"})[1])["id"]
d = json.loads(call("POST", f"/admin/users/{uid}/tokens?scopes=bot,tx")[1])
print(d.get("token") or d.get("api_token") or "")
PY
)
  [ -n "$key" ] || { echo "✗ no se pudo crear la API key"; exit 1; }
  echo "VEXA_API_KEY=$key" >> "$ENV_LOCAL"
fi
echo "✓ Vexa listo en http://127.0.0.1:$GATEWAY_PORT (API key en $ENV_LOCAL)"
