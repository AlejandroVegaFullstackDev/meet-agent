#!/usr/bin/env bash
# Instalador .pkg para macOS (modo extensión): conector + MCP + extensión en /Library/MeetAgent y el conector
# registrado para Chrome, Chromium, Edge, Brave y Firefox de todos los usuarios. La app de escritorio
# no va: todavía no captura audio en macOS.
# Uso (en un Mac): scripts/publish.sh osx-arm64 && scripts/package-macos-pkg.sh osx-arm64 [versión]
# El .pkg no está firmado por Apple: la primera vez, clic derecho → Abrir.
set -euo pipefail
cd "$(dirname "$0")/.."
RID=${1:-osx-arm64}
VERSION="${2:-$(python3 -c "import json;print(json.load(open('extension/manifest.json'))['version'])")}"
SRC=dist/$RID
[ -x "$SRC/meetagent-host" ] || { echo "Falta $SRC/meetagent-host: corre scripts/publish.sh $RID"; exit 1; }
ROOT=$(mktemp -d)
trap 'rm -rf "$ROOT"' EXIT

APP="$ROOT/Library/MeetAgent"
mkdir -p "$APP/bin" "$APP/extension"
cp "$SRC/meetagent-host" "$SRC/meetagent-mcp" "$APP/bin/"
(cd extension && cp -r manifest.json background.js content.js selectors.js assembler.js mic-mixer.js history.html history.js icons "$APP/extension/")
cp deploy/release/LEEME.md docs/mcp.md LICENSE "$APP/"

MAN=$(mktemp -d)
scripts/host-manifests.sh "$MAN" /Library/MeetAgent/bin/meetagent-host
for d in "Library/Google/Chrome/NativeMessagingHosts" "Library/Application Support/Chromium/NativeMessagingHosts" \
         "Library/Microsoft/Edge/NativeMessagingHosts" "Library/Application Support/BraveSoftware/Brave-Browser/NativeMessagingHosts"; do
  mkdir -p "$ROOT/$d"; cp "$MAN/com.meetagent.host.chromium.json" "$ROOT/$d/com.meetagent.host.json"
done
mkdir -p "$ROOT/Library/Application Support/Mozilla/NativeMessagingHosts"
cp "$MAN/com.meetagent.host.firefox.json" "$ROOT/Library/Application Support/Mozilla/NativeMessagingHosts/com.meetagent.host.json"
rm -rf "$MAN"

case "$RID" in osx-arm64) arch=arm64 ;; *) arch=x64 ;; esac
mkdir -p dist/release
out="dist/release/MeetAgent-$VERSION-macos-$arch.pkg"
pkgbuild --root "$ROOT" --identifier dev.alejandrovega.meetagent --version "$VERSION" --install-location / "$out" >/dev/null
echo "$out ($(du -h "$out" | cut -f1))"
