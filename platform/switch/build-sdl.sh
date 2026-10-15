#!/usr/bin/env bash
set -euo pipefail
SCRIPT_DIR=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
REPO_ROOT=$(cd "$SCRIPT_DIR/../.." && pwd)
DEPS_DIR=${KG_SWITCH_DEPS:-"$REPO_ROOT/dist/switch-deps"}
: "${DEVKITPRO:?Install devkitA64/libnx and export DEVKITPRO}"
export PATH="$DEVKITPRO/devkitA64/bin:$DEVKITPRO/tools/bin:$DEVKITPRO/portlibs/switch/bin:$PATH"
export PKG_CONFIG_LIBDIR="$DEVKITPRO/portlibs/switch/lib/pkgconfig"
[[ -f "$DEVKITPRO/portlibs/switch/lib/libvulkan.a" ]] || { printf 'Missing mesa-switch loaderless libvulkan.a.\n' >&2; exit 1; }
cmake -S "$DEPS_DIR/SDL3-Switch" -B "$REPO_ROOT/dist/switch-sdl-build" \
    -DCMAKE_TOOLCHAIN_FILE="$SCRIPT_DIR/switch-toolchain.cmake" \
    -DCMAKE_INSTALL_PREFIX="$REPO_ROOT/dist/switch-sdl-sdk" -DCMAKE_BUILD_TYPE=Release \
    -DSDL_SHARED=OFF -DSDL_STATIC=ON -DSDL_TESTS=OFF -DSDL_EXAMPLES=OFF \
    -DSDL_GPU=ON -DSDL_VULKAN=ON -DSDL_OPENGL=OFF -DSDL_OPENGLES=ON \
    -DSDL_RENDER=OFF -DSDL_JOYSTICK=OFF -DSDL_HAPTIC=OFF -DSDL_HIDAPI=OFF \
    -DSDL_SENSOR=OFF -DSDL_CAMERA=OFF -DSDL_DIALOG=OFF -DSDL_LOADSO=OFF \
    -DSDL_SYSTEM_ICONV=OFF -DSDL_PROCESS=OFF \
    -DHAVE_SYSCONF=OFF -DHAVE_SIGACTION=OFF -DHAVE_FDATASYNC=OFF \
    -DHAVE_ICONV=OFF -DHAVE_PTHREAD_SETNAME_NP=OFF
cmake --build "$REPO_ROOT/dist/switch-sdl-build" --parallel "${KG_BUILD_JOBS:-4}"
cmake --install "$REPO_ROOT/dist/switch-sdl-build"
