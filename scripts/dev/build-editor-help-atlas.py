#!/usr/bin/env python3
"""Regenerate editor help bitmap assets from locally installed Noto Sans CJK SC.

Optional maintainer tool, not a build/runtime dependency. Requires Pillow + fontTools.
No font binaries are copied into the project. Select the proportional SC face (TTC index 2),
not the Mono face (index 7). Default .NET builds use the generated PNG/JSON assets as-is.
"""
from __future__ import annotations
import argparse
import json
import math
from io import BytesIO
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont
from fontTools.ttLib import TTFont

ROOT = Path(__file__).resolve().parents[2]

def build(font_path: Path, index: int, chars: list[str], stem: str, em: int, target: Path | None = None,
          expected_family: str = 'Noto Sans CJK SC', output_family: str | None = None) -> dict:
    # Pillow/FreeType builds do not all read WOFF2 directly. Decode the official
    # webfont in memory for rasterization; no converted font is distributed.
    font_input = str(font_path)
    if font_path.suffix.lower() in {'.woff', '.woff2'}:
        with TTFont(font_path) as decoded:
            decoded.flavor = None
            font_input = BytesIO()
            decoded.save(font_input)
            font_input.seek(0)
    font = ImageFont.truetype(font_input, em, index=index)
    family, style = font.getname()
    if family != expected_family and not family.startswith(expected_family + ' '):
        raise ValueError(f'Expected {expected_family}, got {family!r}. Check --font-index.')
    with TTFont(font_path, fontNumber=index, lazy=True) as source:
        cmap = source.getBestCmap()
        missing = [f'U+{ord(c):04X}' for c in chars if ord(c) not in cmap]
    if missing:
        raise ValueError(f'{font_path.name} does not cover: {", ".join(missing)}')
    ascent, descent = font.getmetrics()
    width, padding = 2048, 8
    placements, metrics = [], {}
    x = y = row_height = 0
    for char in chars:
        left, top, right, bottom = font.getbbox(char, anchor='ls')
        advance = float(font.getlength(char))
        if bottom <= top or right <= left:
            metrics[char] = dict(X=0,Y=0,Width=0,Height=0,Advance=advance,OffsetX=0,OffsetY=0)
            continue
        w, h = right - left + 2 * padding, bottom - top + 2 * padding
        if x + w > width:
            y += row_height
            x, row_height = 0, 0
        alpha = Image.new('L', (w, h))
        ImageDraw.Draw(alpha).text((padding-left, padding-top), char, font=font, fill=255, anchor='ls')
        tile = Image.new('RGBA', (w, h), (255, 255, 255, 0))
        tile.putalpha(alpha)
        placements.append((tile, x, y))
        metrics[char] = dict(X=x,Y=y,Width=w,Height=h,Advance=advance,
                             OffsetX=left-padding,OffsetY=ascent+top-padding)
        x += w
        row_height = max(row_height, h)
    height = math.ceil((y + row_height) / 64) * 64
    atlas = Image.new('RGBA', (width, height), (255,255,255,0))
    for tile, x, y in placements: atlas.paste(tile, (x, y))
    target = target or ROOT / 'Assets/Fonts'
    target.mkdir(parents=True, exist_ok=True)
    atlas.save(target / (stem + '.png'), optimize=True)
    metadata = dict(Family=output_family or family, SourceFamily=family, Style=style,
                    EmSize=em, Ascent=ascent, Descent=descent, Glyphs=metrics)
    (target / (stem + '.json')).write_text(json.dumps(metadata, ensure_ascii=False, separators=(',',':'))+'\n', encoding='utf-8')
    return dict(family=family, style=style, glyphs=len(metrics), width=width, height=height, em=em)

def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--regular', type=Path, default=Path('/usr/share/fonts/opentype/noto/NotoSansCJK-Regular.ttc'))
    parser.add_argument('--bold', type=Path, default=Path('/usr/share/fonts/opentype/noto/NotoSansCJK-Bold.ttc'))
    parser.add_argument('--font-index', type=int, default=2)
    parser.add_argument('--em', type=int, default=64)
    args = parser.parse_args()
    if not 32 <= args.em <= 96: parser.error('--em must be between 32 and 96')
    chars = set(json.loads((ROOT/'Assets/Fonts/cjk-editor.json').read_text(encoding='utf-8')))
    chars.update(chr(c) for c in range(32,127))
    for path in (ROOT/'src/UI/Editor').glob('*.cs'):
        chars.update(c for c in path.read_text(encoding='utf-8') if ord(c)>127 and not c.isspace())
    for path in (ROOT/'src/UI/Localization').glob('*.json'):
        for text in json.loads(path.read_text(encoding='utf-8')).values():
            chars.update(c for c in text if ord(c)>127 and not c.isspace())
    manual = ROOT/'Assets/Documentation/vsm-manual.json'
    if manual.is_file():
        chars.update(c for c in manual.read_text(encoding='utf-8') if ord(c)>127 and not c.isspace())
    chars.update('…')
    ordered = sorted(chars, key=ord)
    for path, stem in ((args.regular, 'editor-help-sans'),(args.bold,'editor-help-sans-bold')):
        if not path.is_file(): parser.error(f'Local font not found: {path}')
        print(json.dumps(build(path, args.font_index, ordered, stem, args.em), ensure_ascii=False))

if __name__ == '__main__': main()
