#!/usr/bin/env python3
"""Bake UI artwork from local, unmodified IBM Plex Sans SC Medium / SemiBold fonts.

Requires Pillow + fontTools. No game resources or font binaries enter the output.
The two generated atlases are embedded in the executable, independent of Assets.
"""
import argparse
import importlib.util
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--regular', type=Path, required=True, help='IBMPlexSansSC-Medium.woff (or TTF/OTF)')
    parser.add_argument('--bold', type=Path, required=True, help='IBMPlexSansSC-SemiBold.woff (or TTF/OTF)')
    parser.add_argument('--font-index', type=int, default=0)
    args = parser.parse_args()
    spec = importlib.util.spec_from_file_location('help_atlas', ROOT / 'scripts/dev/build-editor-help-atlas.py')
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    chars = {chr(code) for code in range(32, 127)}
    chars.update('中文English…')  # language self-names are intentionally never translated
    for path in (ROOT / 'src/UI/Localization').glob('*.json'):
        for text in json.loads(path.read_text(encoding='utf-8')).values():
            chars.update(c for c in text if not c.isspace())
    for font, stem in [(args.regular, 'ui-sans'), (args.bold, 'ui-sans-bold')]:
        if not font.is_file():
            parser.error(f'Font not found: {font}')
        print(json.dumps(module.build(font, args.font_index, sorted(chars, key=ord), stem, 64,
                                      ROOT / 'Resources/Fonts', expected_family='IBM Plex Sans SC',
                                      output_family='Kuroaki UI Sans'), ensure_ascii=False))


if __name__ == '__main__':
    main()
