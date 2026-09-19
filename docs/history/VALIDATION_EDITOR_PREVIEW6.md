# v0.1.1 validation record

## Actual status

This is a source preview, not a compiled or platform-verified release.

The preparation environment has no `dotnet` executable. The actual `dotnet --info` attempt returned `dotnet: command not found`. SDK/compiler downloads were not available. No C# compilation, CPU self-test execution, shader compilation, SDL window creation, macOS test, Windows test or game-side comparison was completed. The original extension DLL/dylib are bundled input files, not newly built outputs.

## Checks actually executed

`python scripts/check-source.py` passed lexical delimiter/type-layout checks on 128 C# files, JSON parsing, object manifest/resource references, and script parsing. This script explicitly does not compile C# or GLSL.

`python scripts/check-editor.py --baseline <original source zip> --extension <original extension zip>` passed 61 offline checks. The result is recorded in `validation-editor.json`.

Those checks confirmed:

- All 156 original Assets files are byte-identical to the uploaded v0.1.0-15.3 source.
- The dependency lock is unchanged.
- The protected Canvas / Shader / Texture / primitive source files were not rewritten.
- No non-Finder file from the source input was removed.
- Both demonstration projects reference files actually present in their directories.
- The demonstration contains complete field sets for the five supported window operation schemas and all five dance presets.
- All 29 non-Finder files from the supplied ExtCustomGimmick archive are bundled byte-identically, including its original native libraries.
- Startup/publish Bash syntax checks passed; no Finder metadata or Python cache remains.

The synthetic demo Vorbis audio was generated with FFmpeg and decoded locally to check its duration. That is an audio-file check, not a test of the C# decoder or player.

## Existing validation material

The original input's older reports, selftest logs, pixel logs and historical fingerprints remain in the package. They describe earlier versions and MUST NOT be treated as evidence that v0.1.1 compiled or ran.

The legacy `check-input-preservation.py` asserts token identity for an older SDL_GPU refactor baseline. It is not an acceptance test for adding the new window ABI declarations and the explicitly changed multi-window `Submit` signature. An earlier invocation against its stored historical fixture failed (extra Canvas members); no historical fixture was rewritten to manufacture a pass. `check-editor.py` instead compares the actual user-uploaded v0.1.0-15.3 assets/protected modules. Neither check establishes behavioral parity.

## Run after installing the SDK

From the project root:

```bash
dotnet build KuroakiGimmick.csproj -c Release
dotnet run --project KuroakiGimmick.csproj -c Release --no-build -- --editor-self-test
dotnet run --project KuroakiGimmick.csproj -c Release --no-build -- --self-test
dotnet run --project KuroakiGimmick.csproj -c Release --no-build -- --gpu-test
dotnet run --project KuroakiGimmick.csproj -c Release --no-build -- --editor Samples/EditorDemo/demo.sgv.json
```

`EditorSelfTest.cs` includes CPU checks for source/BOM/CRLF preservation, repeated source clips, dynamic values, numeric parsing, undo/redo, saving and reopening companion files, external-write protection, BPM conversion, event field completeness, stable backward window evaluation and native ID mapping. These checks are provided as source and were NOT executed in the preparation environment.

## Required platform acceptance tests

Verify Viewer/Editor switching on a real song; select a note, add a gimmick, move and resize it, undo and redo, save a fresh project, reopen and compare at the same timestamps. Test a BPM-change chart and an event after the audio tail. Verify unrecognized VSM lines remain in the saved copy.

In the window demo, first test DESKTOP with LIVE disabled. Then enable LIVE, inspect title/border/geometry/layering, seek backward, repeatedly loop, and close any child window or press Escape. Verify the host remains usable and no native windows are left after exit. Test the separate proxy example, plus different DPI/display arrangements.

Pause/FCAC/result merge, proxyMode00, arbitrary GameMaker surface sources and multi-window video export are not implemented as complete frontend features; see `WINDOW_MOVEMENT_V0.1.1.md`.


## editor-preview.3 track-help changes

- 静态检查确认核心 Note/SV 空轨、原始轨道名、hover 简介和长按 W 详细说明代码存在。
- 当前容器仍无 .NET SDK，因此这些 UI 改动未在此完成 C# 编译或 SDL 交互运行验证。


## editor-preview.4 CJK UI fallback

- Editor/UI lightweight fonts keep their existing Latin glyphs.
- Missing glyphs fall back to bundled `Assets/GameUI/fnt_phosphor.png`; no system font or external font file is required.
- The fallback atlas contains the Chinese characters used by `EditorTrackHelp` and is measured with the same path used for drawing.
- This is an offline/source validation only until a .NET 8 SDK build and runtime check is performed.

## editor-preview.5 dedicated CJK atlas

The editor no longer uses `GameUI/fnt_phosphor` as its missing-glyph fallback. The previous
path could silently fall back to `?` when the large GameUI manifest was unavailable or failed
to initialize. `Assets/Fonts/cjk-editor.png` and `cjk-editor.json` are now copied with the normal
UI assets and are loaded directly by `Fonts`.

The atlas contains the non-ASCII characters currently used by the source UI/help catalogue plus printable ASCII;
`check-editor.py` verifies that every non-ASCII character in `EditorTrackHelp.cs` has a raster
glyph. This remains an offline resource/coverage check, not an SDL_GPU runtime proof.


## editor-preview.6 unified help font

The gimmick hover / hold-W card now opts into one bundled raster face for every glyph in the card: Noto Sans Mono CJK SC Regular. The atlas includes printable ASCII, so `xoffset`, `NoteMotion.X`, `velocity`, numbers and Chinese text no longer switch font family within one line. Viewer UI and raw timeline track labels retain the legacy Kuroaki fonts. This is still an offline/source check until a .NET 8 build and SDL runtime test are performed.
