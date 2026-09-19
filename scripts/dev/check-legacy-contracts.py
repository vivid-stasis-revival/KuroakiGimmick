#!/usr/bin/env python3
"""Offline source/data contract comparison against the supplied r15 source tree.
This does not execute C#, compile shaders, or render the application.
"""
from __future__ import annotations
import argparse
import hashlib
import json
import math
from pathlib import Path
import re
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[2]

def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('baseline', type=Path, help='Extracted input source root (contains Core/Models.cs)')
    args = parser.parse_args()
    baseline = args.baseline.resolve()
    assertions = []
    def check(value, message):
        if not value:
            raise AssertionError(message)
        assertions.append(message)
        print('PASS ' + message)
    def load(path):
        return json.loads(path.read_text(encoding='utf-8-sig'))
    old_models = (baseline/'Core/Models.cs').read_text()
    match = re.search(r'string\[\]\s+Astellion\s*=\s*"([^"]+)"', old_models)
    check(match is not None, 'reference extension table is present in actual baseline source')
    ast = load(ROOT/'Assets/Gimmicks/obj_astellion_gimmick/manifest.json')
    check(ast['extraMods'] == match.group(1).split(), 'all 24 extension IDs match r15 in order, without deduplication')
    check(ast['fixedBpm'] == 174, 'fixed BPM matches r15')
    check(ast['resourcePack'] == 'astellion/manifest.json', 'original resource-pack relative path is preserved')
    check(ast['callbackFades']['bgalph']['duration'] == 9 and ast['callbackFades']['uialpha']['duration'] == 9, 'both first-callback room fades retain nine seconds')
    check(ast['callbackFades']['bgalph']['ease'] == 'outCubic', 'original fade easing is preserved')
    check(ast['modAliases']['holdoverlayalpha'] == 'bgalph', 'hold overlay alpha alias is preserved')
    check(ast['callbackConstraints']['astbars'] == {'integer': True, 'minimum': 1, 'maximum': 4}, 'bar callback index remains an integer in 1..4')
    check(ast['useCommonPostProcessing'] is False, 'original isolated whole-scene postprocessing is retained')
    check(ast['clearProxyFooter'] is False and ast['replaceJudgmentOverlay'] is True, 'proxy footer and fixed judgment decoration capabilities remain intact')
    check(ast['callbackLifetimes']['sides'] == .4, 'callback timeline tail preserves the original .4-second lifetime')
    check([drawing['sortOrder'] for drawing in ast['callbacks']['astbars']] == [1, 2, 3, 4], 'bar callbacks have explicit per-lane composition order')
    legacy_path = baseline/'Assets/Gimmicks/obj_scarletdeath_gimmick/manifest.json'
    fixture = ROOT/'tests/Fixtures/r15-scarlet-definition.json'
    check(fixture.read_bytes() == legacy_path.read_bytes(), 'CPU regression embeds the actual r15 Scarlet definition, not a reconstructed substitute')
    legacy = load(fixture)
    current = load(ROOT/'Assets/Gimmicks/obj_scarletdeath_gimmick/manifest.json')
    templates = load(ROOT/'Assets/Catalog/legacy-per-frame-bindings.json')
    check('useNativeColorControls' not in legacy and current['useNativeColorControls'] is True, 'the missing-vs-false schema difference is present in the real input files')
    check(legacy['version'] == current['version'] == 1, 'both revisions use v1; backward compatibility cannot be gated by different version numbers')
    check(set(legacy['perFrameFunctions']).issubset(templates), 'all legacy function names have declarative compatibility templates')
    check(current['perFrameBindings'] == templates, 'legacy template uses exactly the current scalar expressions')
    check(templates['aberControl']['abx']['beatScale'] == math.pi / 4 and templates['aberControl']['abx']['scale'] == .2 and templates['aberControl']['aby']['scale'] == -.4, 'legacy expression coefficients match the r15 formulas')
    for key in ('extraMods', 'defaults', 'sprites', 'shaders', 'callbacks', 'overlays', 'postModes'):
        check(legacy[key] == current[key], 'Scarlet declaration unchanged: ' + key)
    for name in ('scene_gameplay_scarletdeath.fx.json',):
        check((baseline/'Assets/RoomFX'/name).read_bytes() == (ROOT/'Assets/RoomFX'/name).read_bytes(), 'original room filter values and layer ordering remain byte-identical')
    original_assets = []
    changed_assets = []
    for path in (baseline/'Assets').rglob('*'):
        if not path.is_file() or path.suffix.lower() not in {'.png', '.jpg', '.jpeg', '.frag', '.ttf', '.otf', '.ogg', '.wav'}:
            continue
        relative = path.relative_to(baseline/'Assets')
        target = ROOT/'Assets'/relative
        original_assets.append(str(relative))
        if not target.is_file() or path.read_bytes() != target.read_bytes():
            changed_assets.append(str(relative))
    check(not changed_assets, f'{len(original_assets)} original images/fonts/shaders/audio files preserved byte-for-byte')
    project = ET.parse(ROOT/'KuroakiGimmick.csproj').getroot()
    embeds = {item.attrib.get('Include') for item in project.findall('.//EmbeddedResource')}
    check('Assets/Gimmicks/*/manifest.json' in embeds, 'MSBuild embeds the same object-definition JSON sources')
    check('Assets/Catalog/legacy-per-frame-bindings.json' in embeds, 'legacy expression templates are embedded, not dependent on a new loose file')
    updates = [item for item in project.findall('.//None') if 'Assets/Gimmicks/*/manifest.json' in item.attrib.get('Update', '')]
    check(len(updates) == 1 and updates[0].attrib.get('CopyToOutputDirectory') == updates[0].attrib.get('CopyToPublishDirectory') == 'Always', 'definition metadata cannot stay stale under timestamp-only build copying')
    print(f'{len(assertions)} source/data contract checks passed. C# execution and GPU output NOT tested.')
    return 0

if __name__ == '__main__':
    raise SystemExit(main())
