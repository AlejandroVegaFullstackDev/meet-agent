#!/usr/bin/env bash
# Arma los paquetes para repartir MeetAgent (modo extensión, sin app de escritorio):
#   dist/release/meetagent-extension-<v>.zip     → para cargar a mano (con key: ID fijo)
#   dist/release/meetagent-extension-tienda-<v>.zip → para subir a Chrome Web Store / Edge Add-ons (sin key)
#   dist/release/meetagent-win-x64-<v>.zip       → conector + MCP + extensión + install.ps1 + LEEME.md
#   dist/release/meetagent-linux-x64-<v>.tar.gz  → conector + MCP + extensión + install.sh + LEEME.md
#   dist/release/meetagent-macos-arm64-<v>.tar.gz  (y -x64)  → igual, para macOS
set -euo pipefail
cd "$(dirname "$0")/.."
DOTNET="$HOME/.dotnet/dotnet"; [ -x "$DOTNET" ] || DOTNET="$(command -v dotnet)"
VERSION=$(python3 -c "import json;print(json.load(open('extension/manifest.json'))['version'])")
OUT=dist/release; rm -rf "$OUT"; mkdir -p "$OUT"

# Extensión: solo lo que se instala (sin pruebas ni notas de desarrollo).
EXT_FILES=(manifest.json background.js content.js selectors.js assembler.js mic-mixer.js history.html history.js icons)
python3 - "$OUT/meetagent-extension-$VERSION.zip" "${EXT_FILES[@]}" <<'PY'
import sys, zipfile, os
out, files = sys.argv[1], sys.argv[2:]
with zipfile.ZipFile(out, 'w', zipfile.ZIP_DEFLATED) as z:
    for f in files:
        p = os.path.join('extension', f)
        if os.path.isdir(p):
            for root, _, names in os.walk(p):
                for n in names: z.write(os.path.join(root, n), os.path.relpath(os.path.join(root, n), 'extension'))
        else: z.write(p, f)
PY
# Para subir a Chrome Web Store / Edge Add-ons: igual pero sin "key" (las tiendas la rechazan y asignan su propio ID).
python3 - "$OUT/meetagent-extension-$VERSION.zip" "$OUT/meetagent-extension-tienda-$VERSION.zip" <<'PY'
import sys, zipfile, json
src, out = sys.argv[1], sys.argv[2]
with zipfile.ZipFile(src) as zi, zipfile.ZipFile(out, 'w', zipfile.ZIP_DEFLATED) as zo:
    for n in zi.namelist():
        data = zi.read(n)
        if n == 'manifest.json':
            m = json.loads(data); m.pop('key', None)
            data = json.dumps(m, ensure_ascii=False, indent=2).encode()
        zo.writestr(n, data)
PY

for rid in win-x64 linux-x64 osx-arm64 osx-x64; do
  case "$rid" in osx-arm64) name=macos-arm64 ;; osx-x64) name=macos-x64 ;; *) name="$rid" ;; esac
  pkg="$OUT/meetagent-$name-$VERSION"; mkdir -p "$pkg/bin" "$pkg/extension"
  for proj in MeetAgent.NativeHost MeetAgent.Mcp; do
    "$DOTNET" publish "app/src/$proj" -c Release -r "$rid" --self-contained true -p:PublishSingleFile=true \
      -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=none -o "$pkg/bin" --nologo -v quiet
  done
  find "$pkg/bin" -name '*.pdb' -delete
  (cd "$pkg/extension" && python3 -c "import zipfile,sys; zipfile.ZipFile(sys.argv[1]).extractall('.')" "../../meetagent-extension-$VERSION.zip")
  cp deploy/release/LEEME.md docs/mcp.md "$pkg/"
  cp deploy/extension/chromium-id.txt "$pkg/chromium-ids.txt"   # agregar aquí el ID de la tienda cuando se publique
  if [ "$rid" = win-x64 ]; then
    cp deploy/release/install.ps1 "$pkg/"
    (cd "$OUT" && python3 -c "import shutil,sys; shutil.make_archive(sys.argv[1], 'zip', '.', sys.argv[1])" "meetagent-$name-$VERSION")
  else
    cp deploy/release/install.sh "$pkg/"
    tar -czf "$pkg.tar.gz" -C "$OUT" "meetagent-$name-$VERSION"
  fi
  rm -rf "$pkg"
done
ls -la "$OUT" | tail -n +2
