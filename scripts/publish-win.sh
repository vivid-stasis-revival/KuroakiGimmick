#!/bin/bash
set -euo pipefail
if [ "${1:-}" = --help ]; then
    echo '用法：bash scripts/publish-win.sh [x64|arm64|all]'; echo '可直接在 macOS/Linux 上构建 Windows 包，默认 x64。需要 .NET 8 SDK。'; exit 0
fi
. "$(dirname "${BASH_SOURCE[0]}")/publish-common.sh"
case "${1:-x64}" in
    x64|win-x64) kg_publish win-x64 ;;
    arm64|win-arm64) kg_publish win-arm64 ;;
    all) kg_publish win-x64; kg_publish win-arm64 ;;
    *) echo '架构请选择 x64 / arm64 / all。' >&2; exit 2 ;;
esac
