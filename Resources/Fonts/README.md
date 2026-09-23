# Kuroaki UI font

Interactive-interface text — viewer, editor, documentation and the song info card —
is drawn with **Noto Sans SC**, shipped here as two static weights:

| File | Weight | Role |
| --- | --- | --- |
| `NotoSansSC-Regular.ttf` | 400 | body text |
| `NotoSansSC-SemiBold.ttf` | 600 | headings |

These are real font files, not bitmap artwork. Glyphs are rasterized at run time at the
physical pixel density of whatever is being drawn, so the same string is generated at its
actual size in a 100% DPI window, on a Retina display, and in a 4K card export. **Adding
new interface text never requires regenerating anything.**

Both files are embedded in the executable, so the interface does not depend on the private
`Assets/` directory or on any system font.

## Provenance

Instanced from Google's variable `NotoSansSC[wght].ttf` (`google/fonts`, `ofl/notosanssc`),
whose default master is Thin — reading it directly would render the interface in Thin and
could never reach the heading weight. Regenerate with:

```bash
python3 scripts/dev/make-ui-font.py --variable /path/'NotoSansSC[wght].ttf'
```

That script requires fontTools and is a one-time step when upgrading the upstream font. It
is not part of building or running the application, and it does not depend on interface text.

## Licensing

Noto Sans SC is licensed under SIL OFL 1.1; the full text and copyright are in
`../../ThirdParty/NotoSansSC-OFL.txt`. Its Reserved Font Name is `Source` (the family
derives from Adobe Source Han Sans). This project neither uses that name nor claims one.
The application code remains under its own license.

## Not this font

Scene lyrics and the gameplay HUD deliberately do **not** use Noto. They render through the
target game's own glyph atlases and sprite fonts so that previews match the game; see
`src/Graphics/Text/SceneFont.cs` and `src/Graphics/Text/GameUiRenderer.cs`.
