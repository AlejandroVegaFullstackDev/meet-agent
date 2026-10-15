#!/usr/bin/env bash
# Empaqueta dist/linux-x64 en dist/meetagent_<versión>_amd64.deb (instala en /opt/meetagent).
# Uso: scripts/publish.sh linux-x64 && scripts/package-deb.sh [versión]
set -euo pipefail
cd "$(dirname "$0")/.."
VERSION="${1:-0.1.0}"
SRC=dist/linux-x64
[ -x "$SRC/MeetAgent" ] || { echo "Falta $SRC/MeetAgent: corre scripts/publish.sh linux-x64"; exit 1; }
ROOT=$(mktemp -d)
trap 'rm -rf "$ROOT"' EXIT

mkdir -p "$ROOT/DEBIAN" "$ROOT/opt/meetagent" "$ROOT/usr/bin" "$ROOT/usr/share/applications" \
         "$ROOT/usr/share/icons/hicolor/256x256/apps" "$ROOT/usr/share/icons/hicolor/scalable/apps"
cp -a "$SRC/." "$ROOT/opt/meetagent/"
ln -s /opt/meetagent/MeetAgent "$ROOT/usr/bin/meetagent"
ln -s /opt/meetagent/meetagent-mcp "$ROOT/usr/bin/meetagent-mcp"
cp docs/branding/icon-256.png "$ROOT/usr/share/icons/hicolor/256x256/apps/meetagent.png"
cp docs/branding/icon.svg "$ROOT/usr/share/icons/hicolor/scalable/apps/meetagent.svg"

cat > "$ROOT/usr/share/applications/meetagent.desktop" <<DESK
[Desktop Entry]
Type=Application
Name=MeetAgent
GenericName=Transcriptor de reuniones
Comment=Transcribe tus reuniones en tu equipo (Yo / Otros), con consentimiento
Exec=/opt/meetagent/MeetAgent
Icon=meetagent
Terminal=false
Categories=AudioVideo;Audio;Recorder;
Keywords=reunión;transcripción;whisper;meet;
StartupWMClass=MeetAgent
DESK

cat > "$ROOT/DEBIAN/control" <<CTRL
Package: meetagent
Version: $VERSION
Section: sound
Priority: optional
Architecture: amd64
Maintainer: Alejandro Vega <alejandromatssuno18@gmail.com>
Depends: libc6, libicu74 | libicu72 | libicu70, pulseaudio-utils
Installed-Size: $(du -sk "$ROOT" | cut -f1)
Description: Transcribe tus reuniones en tu propio equipo
 Escucha el micrófono (Yo) y el audio del sistema (Otros) y transcribe en vivo
 con Whisper local. Pregunta antes de grabar. Incluye meetagent-mcp para usarlo
 desde Claude Code, opencode, Codex u otro cliente MCP.
 .
 Los modelos y los datos van en ~/.local/share/MeetAgent/{models,data}
 (o en la carpeta indicada por MEETAGENT_HOME).
CTRL

out="dist/meetagent_${VERSION}_amd64.deb"
dpkg-deb --root-owner-group --build "$ROOT" "$out" >/dev/null
echo "$out ($(du -h "$out" | cut -f1))"
