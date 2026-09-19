# v0.1.1 editor-preview.7 validation record

## Actual status

This is a source preview, not a compiled or platform-verified release. The current environment has no available .NET SDK. No C# compilation, C# CPU self-test, shader compilation, SDL window creation, macOS/Windows execution or game-side comparison was completed for preview.7. The supplied extension DLL/dylib remain input artifacts, not newly built binaries.

## Checks executed for preview.7

- `python -B scripts/check-source.py`: lexical delimiter/type layout, JSON, source manifests, Python/Bash syntax. This is NOT a C# compiler.
- `python -B scripts/check-editor.py --baseline <original source zip> --extension <original extension zip> --report docs/validation-editor.json`: source/resource contracts and original-resource preservation. See the report for the exact checks.
- `python -B scripts/check-help-typography.py`: actual PNG glyph bounds/ink and padding; regular/bold coverage; proportional advances; declared em/ascent/line box; body contrast; no font binaries. See `validation-help-typography.json`.
- Offline 1× and 2× help-card layout illustrations built from the generated PNG/JSON assets, including `uialpha`, `xoffset` and `scrollspeed`, were inspected for legibility and clipping. Pillow resampling is not the SDL sampler; these images are illustrations, NOT macOS screenshots.
- The packaged hotfix was overlaid on the preview.6 source tree and compared against the complete preview.7 file set. Archive CRC and the source hash inventory were checked by the packaging script.

The existing 156 imported Assets and 29 ExtCustomGimmick files remain byte-identical. Dependency declarations/lock, protected Canvas/Shader/Texture/primitive modules and gameplay source are unchanged. New help atlases are generated raster artwork only; no TTF/TTC/OTF or other installed font binaries are included.

## Tests provided but NOT executed here

```bash
dotnet build KuroakiGimmick.csproj -c Release
dotnet run --project KuroakiGimmick.csproj -c Release --no-build -- --editor-self-test
dotnet run --project KuroakiGimmick.csproj -c Release --no-build -- --editor Samples/EditorDemo/demo.sgv.json
```

`HelpTypographySelfTest` is part of `--editor-self-test`. It checks matching proportional font families, em scaling, line-box fit, wrapped line widths/content preservation, Unicode graphemes and measured title ellipses. These C# tests have not run in this environment.

On macOS/Windows verify hover summary, 220ms W hold, body wrapping, W+wheel scroll on long cards, releasing W, and placement at window edges. Check both 1× and Retina/high-DPI output. Confirm the startup log says `Readable help: Noto Sans CJK SC Regular / Bold ... (editor-preview.7)`.

## Historical records

The preview.6 validation document is retained under `docs/history/VALIDATION_EDITOR_PREVIEW6.md`. Older reports, self-test logs and fingerprints bundled in the original source refer to older inputs. They are not evidence that preview.7 compiled or ran. In particular, baseline statements naming an SDK do not identify an SDK used for this revision.

WindowMovement, save/export and gameplay behavior are outside this font-only patch and have not been newly platform-validated. Existing limitations remain; see `WINDOW_MOVEMENT_V0.1.1.md`.
