#!/usr/bin/env bash
# Run from a Linux x86_64 devkitA64 environment after extracting the handoff.
set -euo pipefail
root=$(cd "$(dirname "$0")/../.." && pwd)
cd "$root"
: "${DEVKITPRO:?Set DEVKITPRO to the matching devkitPro installation}"
export PATH="$DEVKITPRO/devkitA64/bin:$DEVKITPRO/tools/bin:$PATH"
export MONO_NX_ROOT="$root/dist/handoff-switch-runtime"
export ICU_NX_INSTALL_DIR="$root/dist/switch-deps/mono-nx/icu/libnx"
export PKG_CONFIG_LIBDIR="$root/dist/switch-sdl-sdk/lib/pkgconfig:$DEVKITPRO/portlibs/switch/lib/pkgconfig"
runtime="$root/dist/switch-llvm-toolchain/runtime"
llvm="$runtime/artifacts/obj/mono/libnx.arm64.Debug/llvm/x64/bin"
export LD_LIBRARY_PATH="$llvm/../lib${LD_LIBRARY_PATH:+:$LD_LIBRARY_PATH}"
build="$root/dist/switch-handoff-llvm-new"
mode=${1:-rebuild}
case "$mode" in
  relink)
    # These exact DLLs and AOT objects shipped with the pending UI r1 build.
    build="$root/dist/switch-llvm-ui-r1"
    ;;
  rebuild)
    [[ ! -e "$build" ]] || { echo "Choose a clean build: $build exists" >&2; exit 1; }
    dotnet publish KuroakiGimmick.csproj -c Release -p:KuroakiSwitch=true \
      -p:KuroakiExternalAssets=true \
      -p:BaseIntermediateOutputPath="$root/dist/handoff-switch-obj/" \
      -p:NuGetLockFilePath="$root/dist/handoff-switch.lock.json" -o "$build/input"
    sdk="$root/dist/switch-deps/mono-nx/dotnet_runtime"
    dotnet "$sdk/artifacts/bin/Mono.Linker/Debug/net9.0/illink.dll" \
      -x "$sdk/src/mono/System.Private.CoreLib/src/ILLink/ILLink.Descriptors.xml" \
      -x "$sdk/src/mono/System.Private.CoreLib/src/ILLink/ILLink.LinkAttributes.xml" \
      -d "$build/input" -d "$sdk/artifacts/bin/mono/libnx.arm64.Debug" \
      -d "$sdk/artifacts/bin/runtime/net9.0-libnx-Debug-arm64" \
      --feature System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported false \
      --feature System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeCompiled false \
      --feature System.Text.Json.JsonSerializer.IsReflectionEnabledByDefault false \
      --feature System.Resources.UseSystemResourceKeys true \
      --skip-unresolved false --trim-mode link -a "$build/input/KuroakiGimmick.dll" -out "$build/managed"
    python3 platform/switch/tools/compile_full_aot.py --managed "$build/managed" \
      --compiler "$runtime/artifacts/bin/mono/libnx.arm64.Debug/cross/libnx-arm64/mono-aot-cross" \
      --llvm-tools "$llvm" --output "$build/objects" --jobs "${KG_BUILD_JOBS:-3}"
    ;;
  *) echo "Usage: $0 [relink|rebuild]" >&2; exit 2 ;;
esac
[[ -f "$MONO_NX_ROOT/artifacts/obj/mono/libnx.arm64.Debug/out/lib/libmonosgen-2.0.a" ]] || {
  echo "Missing materialized TLS-fixed handoff runtime. Use the full handoff package." >&2; exit 1;
}
# Rebuild SDL so pkg-config and build metadata use the new checkout path.
bash platform/switch/build-sdl.sh
python3 platform/switch/tools/prepare_aot_payload.py "$build"
make -C dist/switch-deps/mono-nx/native/interpreter \
  KG_SWITCH_NATIVE="$root/platform/switch/native" KG_SWITCH_AOT="$build" KG_PKG_CONFIG=pkg-config \
  MONO_NX_USE_SDL2=0 MONO_NX_USE_CIMGUI=0 TARGET=KuroakiGimmick-Handoff-LLVM \
  BUILD=build-kg-handoff-llvm ICON="$root/platform/switch/native/icon.jpg" DEFINES=-DKG_SWITCH_TLS_FIX=1
cp dist/switch-deps/mono-nx/native/interpreter/KuroakiGimmick-Handoff-LLVM.nro "$build/"
echo "Built $build/KuroakiGimmick-Handoff-LLVM.nro; hardware validation required."
