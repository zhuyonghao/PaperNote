#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
if command -v dotnet >/dev/null 2>&1; then dotnet_cmd="$(command -v dotnet)"; else dotnet_cmd="$HOME/.dotnet/dotnet"; fi
exec "$dotnet_cmd" run --project src/PaperNote "$@"
