# v0.1.1 editor-preview.8 validation record

## Actual status

This is a source preview, not a compiled or platform-verified release. The current container has no .NET SDK; an attempted download of Microsoft's installer failed because the host could not be resolved. No C# compilation, C# CPU self-test, shader compilation, SDL_GPU rendering or macOS/Windows execution was completed. Supplied extension DLL/dylib files are unchanged input artifacts, not newly compiled binaries.

## Checks actually executed

- `python -B scripts/build-vsm-reference.py --check`: generated index matches the two supplied documents and the committed generator.
- `python -B scripts/check-vsm-reference.py`: independent source row inventory, raw table cell equality, complete chapter/source coverage, source hashes and line ranges, generated template contracts, explicit gaps/conflicts, font coverage, and read-only integration checks. Python regex checks validate the generated contracts, not .NET Regex execution.
- `python -B scripts/check-help-typography.py`: actual PNG ink/bounds/padding, metrics, entire reference/UI character coverage, proportional advances, em/baseline/line box, and high-contrast styling.
- `python -B scripts/check-source.py`: lexical structure, source layout, JSON and Python/Bash syntax. This is NOT a C# compiler.
- `python -B scripts/check-editor.py --baseline <original source> --extension <original extension> --report docs/validation-editor.json`: original-resource preservation and existing editor source contracts.
- Package assembly verifies archive CRC, SHA-256 inventories and that applying the preview.7-to-preview.8 hotfix yields the same files as the complete preview.8 tree. See `validation/preview8-packaging.json` for the packaging result.

The 156 original imported Assets and 29 extension files remain byte-identical. Existing parser, evaluator, scene/GPU renderer and window-motion source files remain unchanged. The reference does not register runtime mods, alter source identifiers, insert events, change project defaults or reinterpret conflicting source documentation.

## Tests included but NOT executed

`--reference-self-test` contains CPU-only C# assertions for deserialization, source hashes and table cells, actual .NET Regex placeholder matching, name/case/ambiguity boundaries, source wording, search and glyph coverage. It is also called by `--editor-self-test`. These tests have not been run in this environment.

```bash
dotnet build KuroakiGimmick.csproj -c Release
dotnet run --project KuroakiGimmick.csproj -c Release --no-build -- --reference-self-test
dotnet run --project KuroakiGimmick.csproj -c Release --no-build -- --editor-self-test
```

Native validation still required: F1/toolbar entry; context lookup from a hovered/selected track; hover/W help, W+wheel, manual search including IME/paste; raw/source toggle, copy and chapter navigation; both scroll areas; window resize/HiDPI; closing without editing or passing keys to the timeline. Check that the startup Build and font logs identify preview.8.

## Historical records

Previous preview.7 validation is retained in `history/VALIDATION_EDITOR_PREVIEW7.md`. Earlier logs are historical and are not evidence of preview.8 compilation or native execution.
