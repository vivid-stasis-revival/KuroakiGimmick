#!/bin/bash
set -euo pipefail
if [ "${1:-}" = --help ]; then
    echo '用法：bash scripts/publish-aot-win.sh [x64|arm64|all]'
    echo '默认 x64；NativeAOT 必须在 Windows 上构建（可使用 Git Bash）。'
    exit 0
fi
. "$(dirname "${BASH_SOURCE[0]}")/publish-aot-common.sh"
case "${1:-x64}" in
    x64|win-x64) kg_aot_publish win-x64 ;;
    arm64|win-arm64) kg_aot_publish win-arm64 ;;
    all) kg_aot_publish win-x64; kg_aot_publish win-arm64 ;;
    *) echo '架构请选择 x64 / arm64 / all。' >&2; exit 2 ;;
esac
