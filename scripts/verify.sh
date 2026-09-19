#!/bin/bash
# Build and run real managed tests. GPU testing is opt-in because it requires
# a supported local Metal/D3D12 device, not merely a successful cross-publish.
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
DOTNET="${KUROAKI_DOTNET:-dotnet}"
GPU=false
if (( $# > 1 )); then
    printf 'Usage: bash scripts/verify.sh [--gpu]\n' >&2
    exit 2
fi
case "${1:-}" in
    '') ;;
    --gpu) GPU=true ;;
    *) printf 'Usage: bash scripts/verify.sh [--gpu]\n' >&2; exit 2 ;;
esac
command -v "$DOTNET" >/dev/null 2>&1 || {
    printf '.NET 8 SDK is required. No build or runtime tests were executed.\n' >&2
    exit 127
}
"$DOTNET" restore "$ROOT/KuroakiGimmick.csproj" --locked-mode
"$DOTNET" build "$ROOT/KuroakiGimmick.csproj" -c Release --no-restore
DLL="$ROOT/bin/Release/net8.0/KuroakiGimmick.dll"
for test in --self-test --editor-self-test --reference-self-test --authoring-self-test --text-film-self-test --custom-adaptation-self-test --native-sequence-self-test --layout-image-self-test --image-object-self-test; do
    "$DOTNET" "$DLL" "$test"
done
if "$GPU"; then
    "$DOTNET" "$DLL" --gpu-test
else
    printf 'SKIP GPU runtime tests; use --gpu on macOS or Windows.\n'
fi
