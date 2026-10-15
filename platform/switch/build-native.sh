#!/usr/bin/env bash
set -euo pipefail
SCRIPT_DIR=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
REPO_ROOT=$(cd "$SCRIPT_DIR/../.." && pwd)
DEPS_DIR=${KG_SWITCH_DEPS:-"$REPO_ROOT/dist/switch-deps"}
: "${DEVKITPRO:?Install devkitA64 and libnx, and export DEVKITPRO}"
export PATH="$DEVKITPRO/devkitA64/bin:$DEVKITPRO/tools/bin:$DEVKITPRO/portlibs/switch/bin:$PATH"
# Use the exact matching mono-nx SDK from gather_sdk.sh, extracted into mono-nx.
export MONO_NX_ROOT="$DEPS_DIR/mono-nx/dotnet_runtime"
export ICU_NX_INSTALL_DIR="$DEPS_DIR/mono-nx/icu/libnx"
[[ -d "$MONO_NX_ROOT/artifacts" ]] || { printf 'Missing mono-nx SDK. Read platform/switch/README.md.\n' >&2; exit 1; }
[[ -f "$DEVKITPRO/portlibs/switch/lib/libvulkan.a" ]] || { printf 'Missing loaderless mesa-switch SDK libvulkan.a.\n' >&2; exit 1; }
export PKG_CONFIG_LIBDIR="$DEVKITPRO/portlibs/switch/lib/pkgconfig"
python3 "$SCRIPT_DIR/tools/generate_sdl_shim.py" --repo "$REPO_ROOT" --sdl "$DEPS_DIR/SDL3-Switch" --out "$SCRIPT_DIR/native/kg_sdl_exports.c"
bash "$SCRIPT_DIR/build-sdl.sh"
# The SDK pkg-config supplies all Mesa's private static dependencies.
export PKG_CONFIG_LIBDIR="$REPO_ROOT/dist/switch-sdl-sdk/lib/pkgconfig:$PKG_CONFIG_LIBDIR"
make -C "$DEPS_DIR/mono-nx/native/interpreter" \
    KG_SWITCH_NATIVE="$SCRIPT_DIR/native" KG_PKG_CONFIG=pkg-config \
    MONO_NX_USE_SDL2=0 MONO_NX_USE_CIMGUI=0 TARGET=KuroakiGimmick \
    ICON="$SCRIPT_DIR/native/icon.jpg"
mkdir -p "$REPO_ROOT/dist/switch-native"
cp "$DEPS_DIR/mono-nx/native/interpreter/KuroakiGimmick.nro" "$REPO_ROOT/dist/switch-native/"
printf 'Built dist/switch-native/KuroakiGimmick.nro. Hardware validation still required.\n'
