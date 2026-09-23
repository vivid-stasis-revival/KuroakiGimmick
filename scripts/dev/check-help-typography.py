#!/usr/bin/env python3
"""Offline help-layout and UI font checks. Does NOT compile C# or initialize SDL.

Optional validation tool requiring fontTools. Standard .NET builds embed the shipped
font files directly and do not invoke this script.
"""
from __future__ import annotations
import json
import re
from pathlib import Path
from fontTools.ttLib import TTFont

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
    # 界面与文档共用运行时栅格化的 Noto Sans SC。这里查的是上游字体本身是否真的缺字——
    # 那是唯一还会让界面出现缺字的情况；字形不再预先烘焙，加文案不需要重新生成任何资源。
    for stem, style in [('NotoSansSC-Regular', 'Regular'), ('NotoSansSC-SemiBold', 'SemiBold')]:
        font = TTFont(ROOT / 'Resources/Fonts' / (stem + '.ttf'))
        upem = font['head'].unitsPerEm
        cmap = font.getBestCmap()
        widths = font['hmtx']
        missing = sorted(c for c in needed if ord(c) not in cmap)
        check(not missing, f'{stem}: current editor CJK and printable ASCII covered'
                           + (f' (missing {"".join(missing[:8])})' if missing else ''))
        check('glyf' in font, f'{stem}: TrueType outlines, the path the renderer rasterizes')
        check('fvar' not in font, f'{stem}: static instance, not a variable font read at its default weight')
        advance = lambda ch: widths[cmap[ord(ch)]][0] / upem
        check(advance('W') > advance('i') * 2, f'{stem}: proportional Latin advances')
        check(abs(advance('中') - 1) < .01, f'{stem}: CJK advances are a full em')
        # 排版按 OS/2 typo 升部定位，界面既有版式是照 0.89em 标定的。
        typo = font['OS/2'].sTypoAscender / upem
        check(.85 <= typo <= .95, f'{stem}: typographic ascent matches the established UI baseline')
        summaries.append(dict(stem=stem, family='Noto Sans SC', style=style,
                              glyphs=font['maxp'].numGlyphs, unitsPerEm=upem,
                              typoAscent=round(typo, 4)))
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
    # 界面字体是随程序集发行的 OFL 字体，允许放在 Resources/Fonts 下；别处仍然不收字体二进制，
    # 以免把系统字体或游戏自带字集混进源码树。Noto Sans SC 的保留字体名是 'Source'，
    # 我们既没有使用该名称，也附带了完整的 OFL 与版权声明。
    forbidden = {'.ttf', '.ttc', '.otf', '.otc', '.woff', '.woff2', '.pfb', '.pfa'}
    licensed = ROOT / 'Resources/Fonts'
    skipped = {ROOT / 'bin', ROOT / 'obj', ROOT / 'dist', licensed}
    strays = [p.relative_to(ROOT) for p in ROOT.rglob('*')
              if p.is_file() and p.suffix.lower() in forbidden
              and not any(root in p.parents for root in skipped)]
    check(not strays, 'font binaries only where their license is declared'
                      + (f' (stray {strays[0]})' if strays else ''))
    check((ROOT / 'ThirdParty/NotoSansSC-OFL.txt').is_file(),
          'shipped UI font carries its OFL text and copyright notice')
    result = dict(scope='offline UI font and help-layout source checks only',
                  compiled=False, gpuExecuted=False, fonts=summaries,
                  specifiedBodyContrast=round(contrast, 3), paletteBodyContrast={k: round(v, 3) for k,v in contrasts.items()}, checks=checks)
    (ROOT / 'docs/validation-help-typography.json').write_text(
        json.dumps(result, ensure_ascii=False, indent=2)+'\n', encoding='utf-8')
    for label in checks:
        print('PASS', label)
    print('No C# compilation or native UI execution was performed by this script.')


if __name__ == '__main__':
    main()
