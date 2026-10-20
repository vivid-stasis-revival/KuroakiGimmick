# Dense preview GPU checks

Build and run on a desktop with the project's SDL/Metal/Vulkan environment and
runtime assets:

```sh
dotnet build scripts/preview-performance/PreviewBench.csproj -c Release \
  -p:PublishAot=false -p:PublishSingleFile=false --artifacts-path /tmp/kg-preview
# Place the matching libkg_native_core beside PreviewBench.dll.
dotnet /tmp/kg-preview/bin/PreviewBench/release/PreviewBench.dll \
  /path/to/chart.vsc preview /tmp/kg-preview-result
```

Modes:

- `preview`: game rendering inside the interactive-preview scope, with forward and
  reverse seeks. Repeated timestamps must have identical pixel checksums.
- `ui`: the actual Viewer drawing code, including panels and the main timeline.
- `editor`: the actual editor drawing code, including the note timeline.
- `layers`: render the same loaded chart at 16, 1,024, 256, then 16 layers.
  Captures candidate counts and checks that returning to 16 reproduces the pixels.
- `full`: full-detail rendering at two timestamps. Use a small prefix for dense
  inputs; this deliberately retains every overlapping note and particle.
- `scope`: full detail, then preview, then full detail on the same renderer.
  The two full-detail frames must match. Ordinary charts must also match the
  preview frame. A fourth frame explicitly disables the preview limit and must
  also match full detail.
- `settings`: click Video, turn STACK LIMIT off, reload the saved preferences,
  verify +/-64 and both Shift modifiers for +/-16, test the bounds, then reset
  defaults to enabled/256 without rebuilding the session.
  Writes a screenshot of the final Video page.

The harness writes JSON timings/counters and PNGs. `recordedMs` includes CPU
recording/submission and any backpressure. `completedMs` additionally waits for
GPU completion and pixel readback. A hidden window is used, so these measurements
are not display FPS or a long-session playback test. The first frame includes
lazy shader/JIT/driver initialization; report it separately. UI checksums vary
with live FPS and UI animation.

Settings and output are isolated to the specified output directory. Loading is
explicitly unlimited and requires the native parser. Run huge inputs with an
RSS/time watchdog. The tests never write to the chart.
