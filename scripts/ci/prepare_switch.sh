#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/../.."
printf '{"sdk":{"version":"9.0.102","rollForward":"disable"}}\n' > global.json
dotnet publish KuroakiGimmick.csproj -c Release \
  -p:KuroakiSwitch=true -p:KuroakiExternalAssets=true \
  -p:BaseIntermediateOutputPath=dist/switch-obj/ -o dist/switch-managed
python3 - <<'PY'
import json
from pathlib import Path
import xml.etree.ElementTree as ET
version = ET.parse('KuroakiGimmick.csproj').findtext('.//Version')
if not version:
    raise SystemExit('Missing application version')
profile = {
    'app_id': 'KuroakiGimmick', 'entry_assembly': 'KuroakiGimmick.dll',
    'title': 'Kuroaki/Gimmick', 'author': 'vivid-stasis-revival',
    'version': version, 'launch_args': '--render-width\n640\n--language\nzh-CN\n',
}
Path('dist/nroforge-profile.json').write_text(json.dumps(profile, indent=2) + '\n')
PY
