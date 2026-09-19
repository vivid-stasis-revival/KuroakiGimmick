#!/bin/bash
set -euo pipefail
if [ "${1:-}" = --help ]; then
    echo '用法：bash scripts/publish-mac.sh [arm64|x64|all]'; echo '默认当前 CPU；all 分别生成两个包。需要 .NET 8 SDK。'; exit 0
fi
. "$(dirname "${BASH_SOURCE[0]}")/publish-common.sh"
case "${1:-$(uname -m)}" in
    arm64|aarch64|osx-arm64) kg_publish osx-arm64 ;;
    x64|x86_64|osx-x64) kg_publish osx-x64 ;;
    all) kg_publish osx-arm64; kg_publish osx-x64 ;;
    *) echo '架构请选择 arm64 / x64 / all。' >&2; exit 2 ;;
esac
