# v0.1.1 editor-preview.9 validation record

## Scope and actual limits

This package is an edited source preview, not a compiled release. No .NET SDK / C# compiler is installed in the working environment. Attempts to fetch the official SDK installer or metadata failed with DNS resolution errors. C# compilation, C# CPU tests, shader compilation, SDL_GPU execution and macOS/Windows interaction were NOT performed.

Original extension DLL/dylib files are unchanged supplied files, not newly built outputs. The layout PNGs are offline Pillow illustrations from the supplied raster glyph atlases and presentation data, not application screenshots.

## Executed checks

- `python -B scripts/dev/build-vsm-reference.py --check`: generated presentation data equals committed data.
- `python -B scripts/dev/check-vsm-reference.py`: all original table rows/cells and source spans, section coverage, source hashes, template contracts, no auto-note or whole-source navigation entries, typed blocks, row targets, internal source integrity and font coverage.
- `python -B scripts/dev/check-help-typography.py`: bitmap ink, bounds, padding, em, baselines, proportional widths, current characters and contrast. Existing atlases are byte-identical to preview.8.
- `python -B scripts/dev/check-source.py`: lexical/structural C#, JSON, Python and shell checks only; not C# type checking.
- `python -B scripts/dev/check-editor.py --baseline <original v0.1.0-15.3 ZIP> --extension <original ECG ZIP>`: imported assets, native extension inventory and prior editor contracts. Canvas is permitted ONLY the default-one scoped vertex-opacity multiplication/reset; removing those two additions must recover the original Canvas files byte-for-byte.
- `python -B scripts/dev/check-ui-presentation.py --baseline <preview.8 ZIP>`: font/resource preservation, unchanged game/evaluation/save modules, UI overlay/input boundaries, source-only changes, generated layout at multiple widths and all table/code content.
- `scripts/dev/render-reference-preview.py`: rendered and visually inspected 1440x960 and 1000x800 offline examples using existing PNG glyphs. This is not an SDL rendering or a native input test.
- Packaging verifies ZIP CRCs, source SHA-256 inventory and complete identity after overlaying the hotfix onto the exact preview.8 base. The final package report is provided alongside the delivery.

## Included, not executed

`--reference-self-test` includes actual C# tests for the embedded JSON deserialization, source cells, .NET regular expressions, help mapping and glyph lookup. `UiPresentationSelfTest` adds finite endpoints, interruption/reversal, frame-rate independence, reduced motion, cache expiration, direct-manipulation snapping, non-finite guards, table geometry, code indentation, grapheme preservation and page anchors. It is also reached from `--editor-self-test`.

```bash
dotnet build KuroakiGimmick.csproj -c Release
dotnet run --project KuroakiGimmick.csproj -c Release --no-build -- --reference-self-test
dotnet run --project KuroakiGimmick.csproj -c Release --no-build -- --editor-self-test
```

Native checks still needed: repeated F1/Esc during opening/closing, compact outline overlay and click blocking, IME/paste/search, both draggable scrollbars, context routing, history positions, table links, code clipboard, buttons/modal reversal, UI transition toggle and persistence, Retina/Windows DPI, exact note and clip dragging during scrolling, desktop-window preview, and video export before/after UI changes.

## Historical records

Earlier validation files describe earlier builds only. Preview.8's previous record is retained in `history/VALIDATION_EDITOR_PREVIEW8.md`. No earlier assertion of validation should be treated as compilation or execution of preview.9.
