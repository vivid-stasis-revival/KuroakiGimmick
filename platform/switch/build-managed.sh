#!/usr/bin/env bash
set -euo pipefail
SCRIPT_DIR=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
REPO_ROOT=$(cd "$SCRIPT_DIR/../.." && pwd)
# No desktop RID, trimming, .NET NativeAOT or desktop native binaries.
dotnet publish "$REPO_ROOT/KuroakiGimmick.csproj" -c Release \
    -p:KuroakiSwitch=true -p:BaseIntermediateOutputPath="$REPO_ROOT/dist/switch-obj/" \
    -o "$REPO_ROOT/dist/switch-managed"
