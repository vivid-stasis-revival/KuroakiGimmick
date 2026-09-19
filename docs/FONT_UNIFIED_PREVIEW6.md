# v0.1.1 editor-preview.6 font pass

The gimmick hover / hold-W card now uses one raster face for the entire card: **Noto Sans Mono CJK SC Regular**.
Printable ASCII is baked into the same `cjk-editor` atlas as CJK glyphs, so identifiers and English fragments inside Chinese prose no longer switch to the legacy DejaVu UI font.

Raw timeline identifiers and the Viewer UI deliberately keep the existing Kuroaki fonts.
No TTF/TTC/OTF font binary is distributed.
