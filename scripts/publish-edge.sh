#!/usr/bin/env bash
# Sube un ZIP a Edge Add-ons y lo envía a revisión (API v1.1). La primera vez el producto se crea a mano en Partner Center.
# Variables: EDGE_PRODUCT_ID, EDGE_CLIENT_ID, EDGE_API_KEY (Partner Center → Edge → Publish API).
# Uso: scripts/publish-edge.sh dist/release/meetagent-extension-tienda-<v>.zip "notas para el revisor"
set -euo pipefail
ZIP=$1; NOTES=${2:-"Nueva versión de MeetAgent."}
API="https://api.addons.microsoftedge.microsoft.com/v1/products/$EDGE_PRODUCT_ID/submissions"
AUTH=(-H "Authorization: ApiKey $EDGE_API_KEY" -H "X-ClientID: $EDGE_CLIENT_ID")

# Espera a que una operación termine; imprime el JSON final y falla si no fue «Succeeded».
esperar() {
  local url=$1 r s
  for _ in $(seq 60); do
    r=$(curl -fsS "${AUTH[@]}" "$url"); s=$(echo "$r" | python3 -c "import json,sys;print(json.load(sys.stdin).get('status',''))")
    [ "$s" = InProgress ] || { echo "$r"; [ "$s" = Succeeded ]; return; }
    sleep 10
  done
  echo "Tiempo agotado esperando $url"; return 1
}
# El ID de la operación viene en la cabecera Location de la respuesta 202.
operacion() { grep -i '^location:' | tr -d '\r' | awk -F/ '{print $NF}'; }

op=$(curl -fsS -D - -o /dev/null "${AUTH[@]}" -H "Content-Type: application/zip" --data-binary "@$ZIP" "$API/draft/package" | operacion)
esperar "$API/draft/package/operations/$op"
op=$(curl -fsS -D - -o /dev/null "${AUTH[@]}" -H "Content-Type: application/json" \
  --data "$(python3 -c "import json,sys;print(json.dumps({'notes':sys.argv[1]}))" "$NOTES")" "$API" | operacion)
esperar "$API/operations/$op"
echo "Enviada a revisión en Edge Add-ons."
