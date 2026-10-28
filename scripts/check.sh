#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
if command -v dotnet >/dev/null 2>&1; then dotnet_cmd="$(command -v dotnet)"; else dotnet_cmd="$HOME/.dotnet/dotnet"; fi
(cd editor && npm run build)
"$dotnet_cmd" build PaperNote.slnx -c Release
"$dotnet_cmd" run --project tests/PaperNote.Tests
(cd editor && npm test -- "$@")
