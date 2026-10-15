#!/usr/bin/env bash
# Registra meetagent-host (native messaging) para la extensión de Meet en Firefox, Edge, Chrome y Chromium.
# Solo escribe un JSON por navegador en la carpeta de tu usuario; no necesita sudo ni abre puertos.
# Uso: scripts/install-extension-host.sh     ·  Desinstalar: scripts/install-extension-host.sh --remove
set -euo pipefail
cd "$(dirname "$0")/.."
NAME=com.meetagent.host
HOST_BIN="$PWD/dist/linux-x64/meetagent-host"
CHROMIUM_ID=$(cat deploy/extension/chromium-id.txt)
GECKO_ID=meetagent@alejandrovega.dev
DIRS_CHROMIUM=("$HOME/.config/google-chrome/NativeMessagingHosts" "$HOME/.config/chromium/NativeMessagingHosts" "$HOME/.config/microsoft-edge/NativeMessagingHosts")
DIRS_FIREFOX=("$HOME/.mozilla/native-messaging-hosts" "$HOME/snap/firefox/common/.mozilla/native-messaging-hosts")

if [ "${1:-}" = "--remove" ]; then
  for d in "${DIRS_CHROMIUM[@]}" "${DIRS_FIREFOX[@]}"; do rm -f "$d/$NAME.json"; done
  echo "Host de MeetAgent desregistrado."; exit 0
fi

[ -x "$HOST_BIN" ] || { echo "Falta $HOST_BIN: corre primero scripts/publish.sh linux-x64"; exit 1; }
for d in "${DIRS_CHROMIUM[@]}"; do
  [ -d "$(dirname "$d")" ] || continue          # solo navegadores que existen
  mkdir -p "$d"
  cat > "$d/$NAME.json" <<JSON
{ "name": "$NAME", "description": "MeetAgent: guarda los subtítulos de Meet en tu equipo", "path": "$HOST_BIN",
  "type": "stdio", "allowed_origins": ["chrome-extension://$CHROMIUM_ID/"] }
JSON
  echo "✓ $d"
done
for d in "${DIRS_FIREFOX[@]}"; do
  # La ruta estándar siempre: el Firefox de snap lanza el host por el portal del sistema, que la busca ahí.
  [ "$d" = "$HOME/.mozilla/native-messaging-hosts" ] || [ -d "$(dirname "$d")" ] || continue
  mkdir -p "$d"
  cat > "$d/$NAME.json" <<JSON
{ "name": "$NAME", "description": "MeetAgent: guarda los subtítulos de Meet en tu equipo", "path": "$HOST_BIN",
  "type": "stdio", "allowed_extensions": ["$GECKO_ID"] }
JSON
  echo "✓ $d"
done
echo "Listo. Ahora carga la extensión (carpeta extension/): ver extension/README.md"
