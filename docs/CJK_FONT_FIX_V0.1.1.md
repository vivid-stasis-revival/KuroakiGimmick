# editor-preview.5 CJK font fix

`editor-preview.4` tried to reuse `Assets/GameUI/fnt_phosphor` through the large
`game-ui.gameui.json` pack as the editor fallback. On the reported macOS build that fallback
was not available at runtime, so every missing sans/mono glyph reached the final `?` branch.

`editor-preview.5` removes that dependency from editor text. It loads two direct assets:

- `Assets/Fonts/cjk-editor.png`
- `Assets/Fonts/cjk-editor.json`

The bitmap atlas contains the non-ASCII characters used by the current source UI/help text.
The original raw gimmick identifiers still use the compact mono/sans UI atlas; only missing
glyphs use this fallback.

`Measure()` and `Text()` use the same glyph table and nominal 32 px metrics. Publish scripts
now fail early if the CJK atlas is missing. If the atlas cannot be loaded for another reason,
`Fonts` writes a `[font] CJK editor atlas ...` line to stderr instead of failing silently.

The atlas is raster output. No system font or font binary is required or distributed by this
package.
