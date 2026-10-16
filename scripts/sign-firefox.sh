#!/usr/bin/env bash
# Firma la extensión en addons.mozilla.org como «no listada» (no se publica) y deja el .xpi en dist/firefox-signed/.
# Pide tus claves de API de AMO sin mostrarlas ni guardarlas: https://addons.mozilla.org/developers/addon/api/key/
set -euo pipefail
cd "$(dirname "$0")/.."
scripts/build-firefox.sh
read -rp  "API key de AMO (empieza con user:): " WEB_EXT_API_KEY
read -rsp "API secret de AMO (no se muestra): " WEB_EXT_API_SECRET; echo
export WEB_EXT_API_KEY WEB_EXT_API_SECRET
npx --yes web-ext@8 sign --source-dir dist/firefox --artifacts-dir dist/firefox-signed --channel unlisted
unset WEB_EXT_API_KEY WEB_EXT_API_SECRET
ls -la dist/firefox-signed/*.xpi && echo "Arrastra ese .xpi a Firefox y pulsa «Añadir»."
