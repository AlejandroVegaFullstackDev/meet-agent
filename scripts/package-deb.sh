#!/usr/bin/env bash
# Empaqueta dist/linux-x64 en dist/release/meetagent_<versión>_amd64.deb (instala en /opt/meetagent).
# Incluye la app de escritorio, el conector de la extensión (registrado para Chrome, Chromium, Edge y Firefox
# en todo el sistema), el MCP y la carpeta de la extensión.
# Uso: scripts/publish.sh linux-x64 && scripts/package-deb.sh [versión]   (por defecto, la del manifest)
set -euo pipefail
cd "$(dirname "$0")/.."
VERSION="${1:-$(python3 -c "import json;print(json.load(open('extension/manifest.json'))['version'])")}"
SRC=dist/linux-x64
[ -x "$SRC/MeetAgent" ] || { echo "Falta $SRC/MeetAgent: corre scripts/publish.sh linux-x64"; exit 1; }
ROOT=$(mktemp -d)
trap 'rm -rf "$ROOT"' EXIT

mkdir -p "$ROOT/DEBIAN" "$ROOT/opt/meetagent/extension" "$ROOT/usr/bin" "$ROOT/usr/share/applications" \
         "$ROOT/usr/share/icons/hicolor/256x256/apps" "$ROOT/usr/share/icons/hicolor/scalable/apps"
cp -a "$SRC/." "$ROOT/opt/meetagent/"
(cd extension && cp -r manifest.json background.js content.js selectors.js assembler.js mic-mixer.js history.html history.js icons "$ROOT/opt/meetagent/extension/")
cp deploy/release/LEEME.md docs/mcp.md LICENSE "$ROOT/opt/meetagent/"
ln -s /opt/meetagent/MeetAgent "$ROOT/usr/bin/meetagent"
ln -s /opt/meetagent/meetagent-mcp "$ROOT/usr/bin/meetagent-mcp"
cp docs/branding/icon-256.png "$ROOT/usr/share/icons/hicolor/256x256/apps/meetagent.png"
cp docs/branding/icon.svg "$ROOT/usr/share/icons/hicolor/scalable/apps/meetagent.svg"

# Native messaging para todos los usuarios (el Firefox de snap no lee estas rutas: ahí sirve install.sh).
MAN=$(mktemp -d)
scripts/host-manifests.sh "$MAN" /opt/meetagent/meetagent-host
for d in etc/opt/chrome/native-messaging-hosts etc/chromium/native-messaging-hosts etc/opt/edge/native-messaging-hosts; do
  mkdir -p "$ROOT/$d"; cp "$MAN/com.meetagent.host.chromium.json" "$ROOT/$d/com.meetagent.host.json"
done
mkdir -p "$ROOT/usr/lib/mozilla/native-messaging-hosts"
cp "$MAN/com.meetagent.host.firefox.json" "$ROOT/usr/lib/mozilla/native-messaging-hosts/com.meetagent.host.json"
rm -rf "$MAN"

cat > "$ROOT/usr/share/applications/meetagent.desktop" <<DESK
[Desktop Entry]
Type=Application
Name=MeetAgent
GenericName=Notas de reuniones con tu IA
Comment=Graba y transcribe tus reuniones y las analiza con tu propia IA (Claude, OpenAI, DeepSeek, Gemini o tu servidor)
Exec=/opt/meetagent/MeetAgent
Icon=meetagent
Terminal=false
Categories=AudioVideo;Audio;Recorder;Office;
Keywords=reunión;transcripción;meet;ia;resumen;
StartupWMClass=MeetAgent
DESK

cat > "$ROOT/DEBIAN/control" <<CTRL
Package: meetagent
Version: $VERSION
Section: sound
Priority: optional
Architecture: amd64
Maintainer: Alejandro Vega <alejandromatssuno18@gmail.com>
Homepage: https://github.com/AlejandroVegaFullstackDev/meet-agent
Depends: libc6, libicu76 | libicu74 | libicu72 | libicu70, pulseaudio-utils
Installed-Size: $(du -sk "$ROOT" | cut -f1)
Description: Notas de reuniones con tu propia IA
 Graba el micrófono y el sonido del equipo (con consentimiento), lo transcribe y
 lo analiza con la IA que elijas: Claude, OpenAI, DeepSeek, Gemini, OpenRouter o
 tu propio servidor (Ollama, LM Studio). Sin modelos locales.
 .
 Incluye el conector de la extensión de Google Meet (subtítulos con el nombre de
 cada persona) y meetagent-mcp para Claude Code, Codex u otro cliente MCP.
 Los datos quedan en ~/.local/share/MeetAgent/data.
CTRL

mkdir -p dist/release
out="dist/release/meetagent_${VERSION}_amd64.deb"
dpkg-deb --root-owner-group --build "$ROOT" "$out" >/dev/null
echo "$out ($(du -h "$out" | cut -f1))"
