#!/usr/bin/env bash
set -euo pipefail
SCRIPT_DIR=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
REPO_ROOT=$(cd "$SCRIPT_DIR/../.." && pwd)
: "${DEVKITPRO:?Set DEVKITPRO}"
export PATH="$DEVKITPRO/devkitA64/bin:$DEVKITPRO/tools/bin:$DEVKITPRO/portlibs/switch/bin:$PATH"
export PKG_CONFIG_LIBDIR="$REPO_ROOT/dist/switch-sdl-sdk/lib/pkgconfig:$DEVKITPRO/portlibs/switch/lib/pkgconfig"
mkdir -p "$REPO_ROOT/dist/switch-probe"
case "${1:-vulkan}" in
    vulkan) target=KG-Vulkan-Probe; source_file=main.c; title="K/G Vulkan + buffer probe r3"; version=0.1.4-r3 ;;
    audio) target=KG-Audio-Probe; source_file=audio.c; title="K/G audio probe r4"; version=0.1.4-r4 ;;
    *) printf 'Usage: build-probe.sh [vulkan|audio]\n' >&2; exit 1 ;;
esac
# pkg-config output is a compiler flag list supplied by trusted local SDK metadata.
# devkitPro's wrapper resets PKG_CONFIG_LIBDIR and cannot see our local SDL SDK.
compile_flags_text=$(pkg-config --cflags sdl3 vulkan)
link_flags_text=$(pkg-config --static --libs sdl3 vulkan)
read -r -a compile_flags <<< "$compile_flags_text"
read -r -a link_flags <<< "$link_flags_text"
arch=(-march=armv8-a+crc+crypto -mtune=cortex-a57 -mtp=soft -fPIE)
aarch64-none-elf-gcc "${arch[@]}" -O2 -D__SWITCH__ \
    -I"$DEVKITPRO/libnx/include" "${compile_flags[@]}" \
    "$SCRIPT_DIR/probe/$source_file" -specs="$DEVKITPRO/libnx/switch.specs" \
    -L"$DEVKITPRO/libnx/lib" -L"$DEVKITPRO/portlibs/switch/lib" \
    -Wl,--start-group "${link_flags[@]}" -lnx -lm -lstdc++ -pthread -Wl,--end-group \
    -o "$REPO_ROOT/dist/switch-probe/$target.elf"
nacptool --create "$title" "vivid-stasis-revival" "$version" "$REPO_ROOT/dist/switch-probe/$target.nacp"
elf2nro "$REPO_ROOT/dist/switch-probe/$target.elf" "$REPO_ROOT/dist/switch-probe/$target.nro" \
    --nacp="$REPO_ROOT/dist/switch-probe/$target.nacp"
