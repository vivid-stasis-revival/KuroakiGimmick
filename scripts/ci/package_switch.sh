#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/../.."
forge=dist/switch-nro
source=dist/nroforge-source
test ! -e artifacts/switch
test -s "$forge/sd/switch/KuroakiGimmick/KuroakiGimmick.nro"
test -s "$forge/sd/mono/etc/icudt77l.dat"
# NroForge receives no Assets input. Reject any unexpected resource payload.
test ! -e "$forge/sd/switch/KuroakiGimmick/Assets"
mkdir -p artifacts
cp -R "$forge/sd" artifacts/switch
mkdir -p artifacts/switch/switch/KuroakiGimmick/Assets
cp "$forge/BUILD-INFO.json" "$forge/aot-manifest.json" artifacts/switch/
cp docs/EXTERNAL_ASSETS.md artifacts/switch/INSTALL-ASSETS.md
cp LICENSE THIRD_PARTY_NOTICES.md artifacts/switch/
cp -R ThirdParty artifacts/switch/
mkdir -p artifacts/switch/ThirdParty/NroForge
cp "$source/LICENSE" "$source/THIRD_PARTY_NOTICES.md" artifacts/switch/ThirdParty/NroForge/
cp -R "$source/licenses" artifacts/switch/ThirdParty/NroForge/
python3 - <<'PY'
import hashlib
import json
from pathlib import Path
root = Path('artifacts/switch')
info = json.loads((root / 'BUILD-INFO.json').read_text())
nro = root / 'switch/KuroakiGimmick/KuroakiGimmick.nro'
if hashlib.sha256(nro.read_bytes()).hexdigest() != info['nro']['sha256']:
    raise SystemExit('Packaged NRO does not match NroForge BUILD-INFO')
if any((root / 'switch/KuroakiGimmick/Assets').iterdir()):
    raise SystemExit('Private Assets must not be packaged')
PY
(cd artifacts && zip -r KuroakiGimmick-switch.zip switch)
