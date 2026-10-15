#!/usr/bin/env bash
set -euo pipefail
SCRIPT_DIR=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
REPO_ROOT=$(cd "$SCRIPT_DIR/../.." && pwd)
DEPS_DIR=${KG_SWITCH_DEPS:-"$REPO_ROOT/dist/switch-deps"}
AOT_DIR=${KG_SWITCH_AOT_DIR:-"$REPO_ROOT/dist/switch-aot-r5"}
: "${DEVKITPRO:?Install devkitA64/libnx and export DEVKITPRO}"
export PATH="$DEVKITPRO/devkitA64/bin:$DEVKITPRO/tools/bin:$DEVKITPRO/portlibs/switch/bin:$PATH"
export MONO_NX_ROOT="$DEPS_DIR/mono-nx/dotnet_runtime"
export ICU_NX_INSTALL_DIR="$DEPS_DIR/mono-nx/icu/libnx"
export PKG_CONFIG_LIBDIR="$REPO_ROOT/dist/switch-sdl-sdk/lib/pkgconfig:$DEVKITPRO/portlibs/switch/lib/pkgconfig"
[[ -f "$AOT_DIR/kg_aot_modules.h" && -f "$AOT_DIR/kg_aot_payload.h" ]] || {
    printf 'Compile the Full AOT closure and prepare its metadata payload first.\n' >&2; exit 1;
}
python3 "$SCRIPT_DIR/tools/generate_sdl_shim.py" --repo "$REPO_ROOT" --sdl "$DEPS_DIR/SDL3-Switch" --out "$SCRIPT_DIR/native/kg_sdl_exports.c"
make -C "$DEPS_DIR/mono-nx/native/interpreter" \
    KG_SWITCH_NATIVE="$SCRIPT_DIR/native" KG_SWITCH_AOT="$AOT_DIR" KG_PKG_CONFIG=pkg-config \
    MONO_NX_USE_SDL2=0 MONO_NX_USE_CIMGUI=0 TARGET=KuroakiGimmick-FullAOT BUILD=build-kg-full-aot \
    ICON="$SCRIPT_DIR/native/icon.jpg"
mkdir -p "$REPO_ROOT/dist/switch-native-aot"
cp "$DEPS_DIR/mono-nx/native/interpreter/KuroakiGimmick-FullAOT.nro" "$REPO_ROOT/dist/switch-native-aot/"
printf 'Built strict Mono Full AOT NRO; hardware coverage/performance still require testing.\n'
