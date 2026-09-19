#!/bin/bash
set -euo pipefail
cd "$(dirname "$0")/.."
DOTNET="${KUROAKI_DOTNET:-dotnet}"
if ! command -v "$DOTNET" >/dev/null 2>&1; then
  echo '需要 .NET 8 SDK；也可设置 KUROAKI_DOTNET 为 dotnet 的完整路径。'
  read -r -p '按 Enter 退出。' _
  exit 1
fi
"$DOTNET" build KuroakiGimmick.csproj -c Release --nologo
"$DOTNET" run --project KuroakiGimmick.csproj -c Release --no-build -- --editor "${1:-Samples/EditorDemo/demo.sgv.json}"
