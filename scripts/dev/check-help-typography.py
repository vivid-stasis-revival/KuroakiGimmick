#!/usr/bin/env python3
"""Offline bitmap/help-layout asset checks. Does NOT compile C# or initialize SDL.

Optional validation tool requiring Pillow. Standard .NET builds use the already
rendered assets and do not invoke this script.
"""
from __future__ import annotations
import json
import math
import re
from pathlib import Path
from PIL import Image

ROOT = Path(__file__).resolve().parents[2]
checks: list[str] = []

def check(condition: bool, label: str) -> None:
    if not condition:
        raise AssertionError(label)
    checks.append(label)


def luminance(rgb: tuple[int, int, int]) -> float:
    channels = [n / 255 for n in rgb]
    channels = [n / 12.92 if n <= .04045 else ((n + .055) / 1.055) ** 2.4 for n in channels]
    return sum(n * w for n, w in zip(channels, (.2126, .7152, .0722)))


def main() -> None:
    needed = set(chr(n) for n in range(32, 127))
    paths = list((ROOT / 'src/UI/Editor').glob('*.cs')) + list((ROOT / 'src/Core/Documentation').glob('*.cs'))
    paths += list((ROOT / 'Assets/Documentation').glob('*.md')) + list((ROOT / 'Assets/Documentation').glob('*.json'))
    for p in paths:
        needed.update(c for c in p.read_text(encoding='utf-8') if ord(c) > 127 and not c.isspace())
    summaries = []
    for stem, style in [('editor-help-sans', 'Regular'), ('editor-help-sans-bold', 'Bold')]:
        metadata = json.loads((ROOT / 'Assets/Fonts' / (stem + '.json')).read_text(encoding='utf-8'))
        glyphs = metadata['Glyphs']
        with Image.open(ROOT / 'Assets/Fonts' / (stem + '.png')) as image:
            check(image.mode == 'RGBA', f'{stem}: RGBA coverage image')
            check(metadata['Family'] == 'Noto Sans CJK SC' and metadata['Style'] == style,
                  f'{stem}: proportional SC face, expected weight')
            check(metadata['EmSize'] == 64, f'{stem}: explicit 64px em')
            check(needed <= glyphs.keys(), f'{stem}: current editor CJK and printable ASCII covered')
            check(glyphs['W']['Advance'] > glyphs['i']['Advance'] * 2,
                  f'{stem}: proportional Latin advances')
            check(abs(glyphs['中']['Advance'] * 17 / metadata['EmSize'] - 17) < .01,
                  f'{stem}: 17-unit CJK is actually a 17-unit em')
            fields = ('X', 'Y', 'Width', 'Height', 'Advance', 'OffsetX', 'OffsetY')
            alpha = image.getchannel('A')
            for char, glyph in glyphs.items():
                if not all(math.isfinite(glyph[key]) for key in fields):
                    raise AssertionError(f'{stem} invalid metrics U+{ord(char):04X}')
                x, y, w, h = (glyph[key] for key in fields[:4])
                if not (x >= 0 and y >= 0 and w >= 0 and h >= 0 and
                        x + w <= image.width and y + h <= image.height):
                    raise AssertionError(f'{stem} glyph out of bounds U+{ord(char):04X}')
                if not char.isspace():
                    bounds = alpha.crop((x, y, x + w, y + h)).getbbox()
                    if bounds is None:
                        raise AssertionError(f'{stem} blank glyph U+{ord(char):04X}')
                    if not (bounds[0] >= 7 and bounds[1] >= 7 and bounds[2] <= w - 7 and bounds[3] <= h - 7):
                        raise AssertionError(f'{stem} insufficient transparent padding U+{ord(char):04X}')
            checks.append(f'{stem}: every glyph finite, in bounds, nonblank, and padded')
            line_box = (metadata['Ascent'] + metadata['Descent']) * 17 / metadata['EmSize']
            check(line_box <= 29, f'{stem}: body line box fits 29-unit spacing')
            summaries.append(dict(stem=stem, family=metadata['Family'], style=style,
                                  glyphs=len(glyphs), width=image.width, height=image.height,
                                  sourceEm=metadata['EmSize'], bodyLineBox=line_box))
    code = (ROOT / 'src/UI/Editor/Viewer.Editor.Help.cs').read_text(encoding='utf-8')
    check('HelpBodySize = 17' in code and 'HelpTitleSize = 22' in code, 'explicit readable body/title sizes')
    check('Canvas.Fill(card, HelpBackground)' in code and 'Color HelpBackground => Theme.HelpBackground;' in code,
          'opaque card uses theme help background, not scene transparency')
    check('Color HelpBodyColor => Theme.Text;' in code, 'body uses theme text color')
    theme = (ROOT / 'src/UI/Viewer.Theme.cs').read_text(encoding='utf-8')
    contrasts = {}
    for name, marker in [('Scarlet', '\"scarlet\" => new('), ('Kuroaki', '\"kuroaki\" => new('), ('Nekomiya', '_ => new(')]:
        values = re.findall(r'Color.Hex\(0x([0-9A-Fa-f]{6})\)', theme.split(marker, 1)[1])[:20]
        check(len(values) == 20, name + ': all opaque palette colors present')
        def rgb(value: str) -> tuple[int, int, int]:
            return tuple(int(value[i:i+2], 16) for i in (0, 2, 4))
        contrasts[name] = (luminance(rgb(values[6])) + .05) / (luminance(rgb(values[17])) + .05)
        check(contrasts[name] >= 12, name + ': help body/background contrast exceeds 12:1')
    contrast = contrasts['Nekomiya']
    check('foreach (var row in layout.Body)' in code and 'EditorHelpLayout.Wrap' in code,
          'wrapped rows drawn without the former five-line cap')
    forbidden = {'.ttf', '.ttc', '.otf', '.otc', '.woff', '.woff2', '.pfb', '.pfa'}
    check(not any(p.is_file() and p.suffix.lower() in forbidden for p in ROOT.rglob('*')),
          'no installed font binaries in source tree')
    result = dict(scope='offline bitmap metrics/ink/source checks only',
                  compiled=False, gpuExecuted=False, atlases=summaries,
                  specifiedBodyContrast=round(contrast, 3), paletteBodyContrast={k: round(v, 3) for k,v in contrasts.items()}, checks=checks)
    (ROOT / 'docs/validation-help-typography.json').write_text(
        json.dumps(result, ensure_ascii=False, indent=2)+'\n', encoding='utf-8')
    for label in checks:
        print('PASS', label)
    print('No C# compilation or native UI execution was performed by this script.')


if __name__ == '__main__':
    main()
