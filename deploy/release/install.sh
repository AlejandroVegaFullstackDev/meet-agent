#!/usr/bin/env bash
# MeetAgent: instalador para Linux (sin sudo). Copia el conector y el MCP a ~/.local/share/MeetAgent/bin
# y los registra para la extensión en Firefox (también el de snap), Chrome, Chromium y Edge.
# Desinstalar: ./install.sh --remove
set -euo pipefail
HERE="$(cd "$(dirname "$0")" && pwd)"
APP="$HOME/.local/share/MeetAgent"
NAME=com.meetagent.host
GECKO_ID=meetagent@alejandrovega.dev
CHROMIUM_IDS="$(cat "$HERE/chromium-ids.txt")"
CHROMIUM_DIRS=("$HOME/.config/google-chrome/NativeMessagingHosts" "$HOME/.config/chromium/NativeMessagingHosts" "$HOME/.config/microsoft-edge/NativeMessagingHosts")
FIREFOX_DIRS=("$HOME/.mozilla/native-messaging-hosts" "$HOME/snap/firefox/common/.mozilla/native-messaging-hosts")

if [ "${1:-}" = "--remove" ]; then
  for d in "${CHROMIUM_DIRS[@]}" "${FIREFOX_DIRS[@]}"; do rm -f "$d/$NAME.json"; done
  rm -rf "$APP/bin"
  echo "MeetAgent desinstalado. Tus reuniones siguen en $APP/data (bórralas a mano si quieres)."; exit 0
fi

mkdir -p "$APP/bin" "$APP/data"
install -m 755 "$HERE/bin/meetagent-host" "$HERE/bin/meetagent-mcp" "$APP/bin/"
HOST="$APP/bin/meetagent-host"
origins=$(for id in $CHROMIUM_IDS; do printf '"chrome-extension://%s/",' "$id"; done | sed 's/,$//')
for d in "${CHROMIUM_DIRS[@]}"; do
  [ -d "$(dirname "$d")" ] || continue
  mkdir -p "$d"
  printf '{ "name": "%s", "description": "MeetAgent", "path": "%s", "type": "stdio", "allowed_origins": [%s] }\n' "$NAME" "$HOST" "$origins" > "$d/$NAME.json"
done
for d in "${FIREFOX_DIRS[@]}"; do
  [ "$d" = "$HOME/.mozilla/native-messaging-hosts" ] || [ -d "$(dirname "$d")" ] || continue
  mkdir -p "$d"
  printf '{ "name": "%s", "description": "MeetAgent", "path": "%s", "type": "stdio", "allowed_extensions": ["%s"] }\n' "$NAME" "$HOST" "$GECKO_ID" > "$d/$NAME.json"
done
echo "✓ MeetAgent instalado en $APP"
echo "  Reuniones: $APP/data/meetagent.db"
echo "  Falta: cargar la extensión (ver LEEME.md) y, si usas IA por MCP:"
echo "    claude mcp add meetagent -- $APP/bin/meetagent-mcp"
