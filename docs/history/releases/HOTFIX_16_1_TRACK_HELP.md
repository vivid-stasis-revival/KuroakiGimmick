# v0.1.2 / 16.1 track-help hover stability hotfix

Fixes the editor track-help popup repeatedly appearing/disappearing while the pointer is kept on a gimmick label.

Cause: the popup was included in `UiOverlayVisible`, while the timeline used `UiOverlayVisible` to decide whether it should keep supplying the hovered track. Showing the popup therefore cleared its own hover source on the next frame.

Fix:
- Split blocking overlays from track-help visibility.
- Keep the track-help source active while the popup itself is visible.
- Treat the popup as part of the hover region and add a 140 ms bridge across the small label-to-popup gap.
- Keep W+wheel scrolling and F1 lookup bound to the latched track while the pointer is over the popup.
- Timeline editing remains disabled while the popup is visible; resize grips stay blocked by the popup.

No game rendering, VSM evaluation, theme values, fonts, export logic, or chart data are changed.
