#!/usr/bin/env bash
# Escribe los manifests de native messaging del conector (uno para Chromium y otro para Firefox) en una carpeta.
# Uso: scripts/host-manifests.sh <carpeta> <ruta del meetagent-host>
#   En Windows la ruta puede ser relativa al manifest (p. ej. meetagent-host.exe); en Linux y macOS, absoluta.
set -euo pipefail
cd "$(dirname "$0")/.."
OUT=$1; HOST=$2
mkdir -p "$OUT"
# python3 o python, el primero que funcione (en Windows python3 puede ser el atajo de la Microsoft Store).
for PYTHON in python3 python; do "$PYTHON" -c "" 2>/dev/null && break; done
"$PYTHON" - "$OUT" "$HOST" <<'PY'
import json, sys, os
out, host = sys.argv[1], sys.argv[2]
ids = [l.strip() for l in open('deploy/extension/chromium-id.txt') if l.strip()]
base = {"name": "com.meetagent.host", "description": "MeetAgent: guarda los subtítulos de Meet en tu equipo", "path": host, "type": "stdio"}
for name, extra in [("chromium", {"allowed_origins": [f"chrome-extension://{i}/" for i in ids]}),
                    ("firefox", {"allowed_extensions": ["meetagent@alejandrovega.dev"]})]:
    with open(os.path.join(out, f"com.meetagent.host.{name}.json"), "w", encoding="utf-8", newline="\n") as f:
        json.dump({**base, **extra}, f, ensure_ascii=False, indent=2)
PY
