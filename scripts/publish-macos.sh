#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
rid="${1:-osx-arm64}"
if [[ "$rid" != osx-arm64 && "$rid" != osx-x64 ]]; then echo "Use osx-arm64 or osx-x64"; exit 1; fi
if command -v dotnet >/dev/null 2>&1; then dotnet_cmd="$(command -v dotnet)"; else dotnet_cmd="$HOME/.dotnet/dotnet"; fi
app="artifacts/$rid/纸间.app"
mkdir -p "$app/Contents/MacOS" "$app/Contents/Resources"
"$dotnet_cmd" publish src/PaperNote -c Release -r "$rid" --self-contained true -o "$app/Contents/MacOS" -p:PublishSingleFile=false
cp packaging/PaperNote.icns "$app/Contents/Resources/PaperNote.icns"
cp packaging/Info.plist "$app/Contents/Info.plist"
chmod +x "$app/Contents/MacOS/PaperNote"
if command -v codesign >/dev/null 2>&1; then codesign --force --deep --sign - "$app"; fi
registrar="/System/Library/Frameworks/CoreServices.framework/Frameworks/LaunchServices.framework/Support/lsregister"
if [[ -x "$registrar" ]]; then "$registrar" -f "$app"; fi
printf 'App: %s\n' "$PWD/$app"
