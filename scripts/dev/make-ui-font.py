#!/usr/bin/env python3
"""Instance the Noto Sans SC variable font into the two static weights the UI ships.

Requires fontTools. Run once when upgrading the upstream font; it is not part of
building or running the application, and it does not depend on the interface text.
The UI rasterizes these files at runtime, so adding new strings never needs a rerun.

Upstream NotoSansSC[wght].ttf defaults to wght=100 (Thin). Reading it directly would
render the interface in Thin and could never reach the heading weight, so both weights
are baked out as static instances instead.
"""
import argparse
from pathlib import Path

from fontTools.ttLib import TTFont
from fontTools.varLib import instancer

ROOT = Path(__file__).resolve().parents[2]
WEIGHTS = {'Regular': 400, 'SemiBold': 600}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--variable', type=Path, required=True, help='NotoSansSC[wght].ttf from google/fonts')
    parser.add_argument('--out', type=Path, default=ROOT / 'Resources/Fonts')
    args = parser.parse_args()

    source = TTFont(args.variable)
    if 'fvar' not in source:
        raise SystemExit(f'{args.variable} is not a variable font.')
    if 'glyf' not in source:
        raise SystemExit('Expected TrueType (glyf) outlines; the CFF path is not what the renderer reads.')

    args.out.mkdir(parents=True, exist_ok=True)
    for name, weight in WEIGHTS.items():
        font = TTFont(args.variable)
        instancer.instantiateVariableFont(font, {'wght': weight}, inplace=True, updateFontNames=True)
        target = args.out / f'NotoSansSC-{name}.ttf'
        font.save(target)
        print(f'{name:9} wght={weight}  {target.relative_to(ROOT)}  {target.stat().st_size:,} bytes')


if __name__ == '__main__':
    main()
