KuroakiGimmick v0.1.2 / 16.2 - Report UI layout hotfix 3

Fixes the Viewer Compatibility details panel still using the old fixed 282px layout.
- INSPECT/REPORT tabs use the current right-panel width without overflowing.
- Diagnostic text wraps by measured glyph width instead of fixed 32-character chunks.
- Visible rows adapt to the actual window height.
- PREV/NEXT and SAVE REPORT are anchored to the current bottom edge instead of fixed Y=590/630.
- Right-panel resize and global UI scale now reflow the report content instead of only scaling the outer workspace.
- No changes to scene rendering, VSM/VSP, editor image objects, export, themes, fonts, or window movement.
