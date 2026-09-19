# editor-preview.9 compile hotfix 1

Fixes two compile errors observed when applying the preview.8 -> preview.9 delta update to an existing working tree:

- Re-ships `src/Native/Sdl.Editor.cs`, including the SDL3 `SDL_SetClipboardText` declaration required by the F1 code-block copy action.
- Makes the conditional search-box fill color explicitly `uint`, matching `Color.Hex(uint, float)`.

No editor document, parser, evaluator, renderer, save format, or window-motion behavior is changed.
