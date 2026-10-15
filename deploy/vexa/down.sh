#!/usr/bin/env bash
# Para Vexa y el STT local. Los datos (volúmenes y .env.local) se conservan.
set -uo pipefail
cd "$(dirname "$0")/../.."
docker rm -f meetagent-vexa meetagent-vexa-stt meetagent-vexa-storage meetagent-vexa-postgres >/dev/null 2>&1 && echo "contenedores parados"
echo "Para borrar también los datos: docker volume rm meetagent-vexa-pgdata meetagent-vexa-storage && rm deploy/vexa/.env.local"
