#!/usr/bin/env bash
# A dedicated, disposable Linux x64 runner is required; never reuse prebuilt app DLLs.
set -euo pipefail
[[ $(uname -s) == Linux && $(uname -m) == x86_64 ]] || { echo 'Switch toolchain requires Linux x64' >&2; exit 1; }
: "${KG_SWITCH_TOOLCHAIN_ROOT:?Set the full handoff root containing dist/}"
: "${DEVKITPRO:?Set the matching devkitPro installation}"
root=$(cd "$(dirname "$0")/../.." && pwd)
cd "$root"
for part in handoff-switch-runtime switch-llvm-toolchain/runtime switch-deps/mono-nx switch-deps/SDL3-Switch; do
  [[ -d "$KG_SWITCH_TOOLCHAIN_ROOT/dist/$part" ]] || { echo "Missing toolchain component: dist/$part" >&2; exit 1; }
done
[[ ! -e dist ]] || { echo 'Use a clean isolated checkout for Switch CI' >&2; exit 1; }
# Copy only required toolchain components. Builds may rewrite their intermediate paths.
for part in handoff-switch-runtime switch-llvm-toolchain/runtime switch-deps/mono-nx switch-deps/SDL3-Switch; do
  mkdir -p "dist/$part"
  rsync -a --exclude='Assets/' --exclude='*.jks' --exclude='*.keystore' \
    "$KG_SWITCH_TOOLCHAIN_ROOT/dist/$part/" "dist/$part/"
done
python3 - <<'PY'
from PIL import Image
Image.open('Assets/App/Kuroaki.png').convert('RGB').resize((256,256)).save('platform/switch/native/icon.jpg')
PY
bash platform/handoff/build-switch-llvm.sh rebuild
mkdir -p artifacts/switch/switch/KuroakiGimmick/Assets artifacts/switch/mono/etc
cp dist/switch-handoff-llvm-new/KuroakiGimmick-Handoff-LLVM.nro artifacts/switch/switch/KuroakiGimmick/KuroakiGimmick.nro
cp docs/EXTERNAL_ASSETS.md artifacts/switch/INSTALL-ASSETS.md
cp THIRD_PARTY_NOTICES.md artifacts/switch/
cp -R ThirdParty artifacts/switch/
# Full AOT embeds assembly metadata, but ICU data is loaded from this SD path.
icu=dist/switch-deps/mono-nx/icu/libnx/share/icu/77.1/icudt77l.dat
test -s "$icu"
cp "$icu" artifacts/switch/mono/etc/
cp dist/switch-deps/mono-nx/LICENSE artifacts/switch/ThirdParty/mono-nx-LICENSE.txt
cp dist/switch-deps/mono-nx/dotnet_runtime/LICENSE.TXT artifacts/switch/ThirdParty/dotnet-runtime-LICENSE.txt
python3 - <<'PY_NRO'
from pathlib import Path
import struct
p = Path('artifacts/switch/switch/KuroakiGimmick/KuroakiGimmick.nro')
header = p.read_bytes()[:128]
assert len(header) == 128 and header[16:20] == b'NRO0', 'Invalid NRO header'
assert 128 <= struct.unpack_from('<I', header, 24)[0] <= p.stat().st_size, 'Invalid NRO size'
PY_NRO
test -s artifacts/switch/switch/KuroakiGimmick/KuroakiGimmick.nro
(cd artifacts && zip -r KuroakiGimmick-switch.zip switch)
