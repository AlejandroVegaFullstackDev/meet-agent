#!/usr/bin/env bash
# Registra meetagent-host (native messaging) para la extensión de Meet en Firefox, Edge, Chrome, Chromium y Brave,
# en Linux (también el Firefox de snap) y macOS. Solo escribe un JSON por navegador en la carpeta de tu usuario;
# no necesita sudo ni abre puertos.
# Uso: scripts/install-extension-host.sh     ·  Desinstalar: scripts/install-extension-host.sh --remove
set -euo pipefail
cd "$(dirname "$0")/.."
NAME=com.meetagent.host
GECKO_ID=meetagent@alejandrovega.dev
# Un ID por línea: el de la key fija (carga a mano) y los que asignan las tiendas (Edge Add-ons, Chrome Web Store).
ORIGINS=$(grep -v '^\s*$' deploy/extension/chromium-id.txt | sed 's#.*#"chrome-extension://&/"#' | paste -sd, -)

if [ "$(uname -s)" = "Darwin" ]; then
  case "$(uname -m)" in arm64) RID=osx-arm64 ;; *) RID=osx-x64 ;; esac
  DIRS_CHROMIUM=("$HOME/Library/Application Support/Google/Chrome/NativeMessagingHosts" \
                 "$HOME/Library/Application Support/Chromium/NativeMessagingHosts" \
                 "$HOME/Library/Application Support/Microsoft Edge/NativeMessagingHosts" \
                 "$HOME/Library/Application Support/BraveSoftware/Brave-Browser/NativeMessagingHosts")
  DIRS_FIREFOX=("$HOME/Library/Application Support/Mozilla/NativeMessagingHosts")
  DIR_ESTANDAR="$HOME/Library/Application Support/Mozilla/NativeMessagingHosts"
else
  RID=linux-x64
  DIRS_CHROMIUM=("$HOME/.config/google-chrome/NativeMessagingHosts" "$HOME/.config/chromium/NativeMessagingHosts" \
                 "$HOME/.config/microsoft-edge/NativeMessagingHosts" "$HOME/.config/BraveSoftware/Brave-Browser/NativeMessagingHosts")
  DIRS_FIREFOX=("$HOME/.mozilla/native-messaging-hosts" "$HOME/snap/firefox/common/.mozilla/native-messaging-hosts")
  DIR_ESTANDAR="$HOME/.mozilla/native-messaging-hosts"
fi
HOST_BIN="$PWD/dist/$RID/meetagent-host"

if [ "${1:-}" = "--remove" ]; then
  for d in "${DIRS_CHROMIUM[@]}" "${DIRS_FIREFOX[@]}"; do rm -f "$d/$NAME.json"; done
  echo "Host de MeetAgent desregistrado."; exit 0
fi

[ -x "$HOST_BIN" ] || { echo "Falta $HOST_BIN: corre primero scripts/publish.sh $RID"; exit 1; }
for d in "${DIRS_CHROMIUM[@]}"; do
  [ -d "$(dirname "$d")" ] || continue          # solo navegadores que existen
  mkdir -p "$d"
  cat > "$d/$NAME.json" <<JSON
{ "name": "$NAME", "description": "MeetAgent: guarda los subtítulos de Meet en tu equipo", "path": "$HOST_BIN",
  "type": "stdio", "allowed_origins": [$ORIGINS] }
JSON
  echo "✓ $d"
done
for d in "${DIRS_FIREFOX[@]}"; do
  # La ruta estándar siempre: el Firefox de snap lanza el host por el portal del sistema, que la busca ahí.
  [ "$d" = "$DIR_ESTANDAR" ] || [ -d "$(dirname "$d")" ] || continue
  mkdir -p "$d"
  cat > "$d/$NAME.json" <<JSON
{ "name": "$NAME", "description": "MeetAgent: guarda los subtítulos de Meet en tu equipo", "path": "$HOST_BIN",
  "type": "stdio", "allowed_extensions": ["$GECKO_ID"] }
JSON
  echo "✓ $d"
done
echo "Listo. Ahora carga la extensión (carpeta extension/): ver extension/README.md"
