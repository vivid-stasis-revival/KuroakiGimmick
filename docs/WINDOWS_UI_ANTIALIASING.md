# Windows UI text antialiasing (r20)

## Root cause

The UI font atlases are authored around a 32 px design size. On a 100% DPI Windows display, 9-12 px labels heavily minify those atlases. A bilinear sampler without mip levels undersamples the glyph alpha coverage, so thin stems and diagonals alias. Retina masks the same defect because the physical destination contains roughly twice as many pixels.

## Fix

1. `Fonts` requests mipmapped linear textures for `sans.png` and `mono.png`.
2. `Texture` builds a premultiplied-alpha-aware mip chain and configures linear mip filtering.
3. `GpuDevice.Transfers` uploads each mip with the existing Metal-tight / D3D12-256-byte row-pitch rules.
4. Text baseline origins snap to the active physical pixel grid without quantizing glyph advances.
5. The UI composition target uses linear sampling if a swapchain-size rounding difference occurs. The 320x180 game preview remains NEAREST.
6. Windows publishes an explicit PerMonitorV2 DPI manifest to prevent OS bitmap virtualization.

No MSAA was added because the glyph edge lives in texture alpha rather than polygon geometry.
