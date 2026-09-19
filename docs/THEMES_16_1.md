# 16.1 UI themes and help-card layering

Settings now offers three UI themes:

- **Nekomiya** — the current 16.1 blue/graphite high-contrast palette and the default for new or old settings files without a theme.
- **Scarlet** — the legacy red/near-black palette used before the 16.0 contrast pass.
- **Kuroaki** — the legacy red/black identity with brighter text, stronger borders and lifted surfaces for readability.

Theme selection is a device/viewing preference. It does not modify the chart, VSM, VSP, rendered scene, or video export.

The workspace divider grips are now suppressed while the hover/W track-help card is visible, including its fade-out interval. Their hit testing is blocked through the same overlay state, so a divider cannot be dragged through the help card.
