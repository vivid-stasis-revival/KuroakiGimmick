#!/bin/bash
set -euo pipefail
if [ "${1:-}" = --help ]; then
    echo '用法：bash scripts/publish-aot-mac.sh [arm64|x64|all]'
    echo '默认当前 CPU；NativeAOT 必须在 macOS 上构建。'
    exit 0
fi
. "$(dirname "${BASH_SOURCE[0]}")/publish-aot-common.sh"
case "${1:-$(uname -m)}" in
    arm64|aarch64|osx-arm64) kg_aot_publish osx-arm64 ;;
    x64|x86_64|osx-x64) kg_aot_publish osx-x64 ;;
    all) kg_aot_publish osx-arm64; kg_aot_publish osx-x64 ;;
    *) echo '架构请选择 arm64 / x64 / all。' >&2; exit 2 ;;
esac
