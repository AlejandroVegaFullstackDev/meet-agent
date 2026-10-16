#!/usr/bin/env bash
# Prepara dist/firefox/: la extensión con el manifest que acepta addons.mozilla.org (AMO).
#  - sin "key" (es solo para fijar el ID en Chrome/Edge)
#  - background con "scripts" (Firefox no usa service_worker)
#  - declara que la extensión no recopila datos para nadie (todo queda en el equipo)
set -euo pipefail
cd "$(dirname "$0")/.."
OUT=dist/firefox
rm -rf "$OUT"; mkdir -p "$OUT"
cp -r extension/{background.js,content.js,selectors.js,assembler.js,mic-mixer.js,history.html,history.js,icons} "$OUT/"
python3 - "$OUT/manifest.json" <<'PY'
import json, sys
m = json.load(open('extension/manifest.json'))
m.pop('key', None)
m['background'] = {'scripts': ['background.js']}
gecko = m.setdefault('browser_specific_settings', {}).setdefault('gecko', {})
gecko['data_collection_permissions'] = {'required': ['none']}
json.dump(m, open(sys.argv[1], 'w'), ensure_ascii=False, indent=2)
PY
echo "Listo: $OUT (versión $(python3 -c "import json;print(json.load(open('$OUT/manifest.json'))['version'])"))"
