#!/usr/bin/env bash
# Genera ejecutables autónomos de MeetAgent Desktop (no requieren .NET instalado) en dist/<rid>/.
# Uso: scripts/publish.sh [linux-x64] [win-x64] ...   (por defecto: linux-x64 win-x64)
set -euo pipefail
cd "$(dirname "$0")/.."
DOTNET="${DOTNET:-$(command -v dotnet || echo "$HOME/.dotnet/dotnet")}"
RIDS=("${@:-linux-x64 win-x64}")
for rid in ${RIDS[@]}; do
  out="dist/$rid"
  rm -rf "$out"
  # Single file para lo administrado; las nativas (Whisper, Skia, SQLite) quedan como archivos al lado
  # porque Whisper.net las busca en runtimes/<rid>/ junto al ejecutable.
  "$DOTNET" publish app/src/MeetAgent.Desktop -c Release -r "$rid" --self-contained true \
    -p:PublishSingleFile=true -p:DebugType=none -o "$out" --nologo -v quiet
  # Quitar nativas de otras plataformas y el shader de Metal (solo macOS).
  find "$out/runtimes" -mindepth 1 -maxdepth 1 -type d ! -name "$rid" -exec rm -rf {} + 2>/dev/null || true
  rm -f "$out/ggml-metal.metal"
  find "$out" -name "*.pdb" -delete
  echo "$rid → $out ($(du -sh "$out" | cut -f1))"
done
