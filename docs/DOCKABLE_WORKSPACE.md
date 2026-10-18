# Dockable workspace

The feature is **off by default**, including upgrades with existing dock trees. Enable it under **Settings → Workspace → Dockable multi-window workspace (experimental)**. Disabling closes native tool windows without discarding their layout, and restores the original single-window geometry, resize grips and Layout dialog.

The desktop workspace uses the existing SDL3/SDL_GPU UI and one application session. Viewer and Editor each own a `DockLayout` tree; panels are identified by stable IDs and contain no chart, transport, or editor state. Every detached root belongs to a real, decorated `SDL_Window` claimed by the existing `GpuDevice`. Window Motion previews stay in their separate `NativeWindowPreview` registry.

## Layout and rendering

- `DockTabs` groups panels; `DockSplit` divides an area horizontally or vertically. Each root can be moved between native windows. A window can contain multiple tabs and splits.
- `DockLayout.Solve` produces panel and splitter rectangles without SDL or GPU calls. Ratios stay in the tree when a window is too small; the solver temporarily displays a compact tab group. Native minimum sizes are computed from the entire tree, including both children of a split.
- The default main window retains the original panel headings, source tabs, and combined Inspector/Export column. Additional tab bars appear for custom mixed groups and detached windows. Both paths use solved panel boundaries and splitters. Compact groups remember tab selection without changing the saved tree. Legacy geometry is imported once, so it cannot overwrite subsequent splitter edits.
- Toolbar workspace changes are deferred to the next frame; a frame never dispatches Viewer panel IDs to the Editor renderer. A root containing only Preview shows just the full game texture in its client area, while retaining the native system title bar, close/minimize/maximize controls and resize border. Right-click the picture for docking or Show Window Controls; Ctrl/Cmd-drag docks the panel directly. Adding another panel restores dock tabs. The window title uses readable panel and song names, never root IDs. Editor image/text canvas and scene picking remain available with dock controls enabled.
- The main Viewer/Editor draws through the panel rects. Auxiliary windows dispatch the same panel drawing functions with their own viewport and pointer state. Scene resources and playback are updated once and reused by every window.
- Auxiliary presentation uses nonblocking swapchain acquisition. Closing a window submits pending commands, waits for that window's presentation, releases it from the shared GPU device, then destroys it.

## Input and persistence

- SDL events are routed by WindowID. Each workspace window keeps its own logical pointer, click/hold state, panel rectangles, active panel, menu state, and UI scale. Text input remembers the SDL window where editing began.
- Dragging a panel title or tab beyond the threshold starts one `DockDragSession`. It supports left/right/top/bottom splits, center tab merge, cross-window transfer, and release outside targets to create a native window. Esc and focus loss cancel the transaction and release capture. The panel menu provides Detach, Dock to Main, Move to Window, Reset Position, and Hide; it is the reliable cross-window fallback when global mouse coordinates are unavailable.
- Device-local `WorkspaceLayout` stores separate Viewer/Editor trees. Named presets live in `ViewerSettings`, never in song or VSM/VSC files. The flat versioned format validates node count/depth, cycles, ratios, panel uniqueness, window bounds, and UI scale. Invalid files fall back to default layouts. Saved windows are clamped onto connected displays without rewriting their saved geometry.
- Switching modes hides the inactive mode's native windows and restores them when returning. Closing an auxiliary window rehomes its panels; closing the main window retains the existing unsaved-edit protection.

## Verification

Use `--dock-layout-test` for the CPU-only tree, persistence, compact-layout, and display-recovery checks. With a desktop display and the sample assets available, run:

```sh
dotnet run --project KuroakiGimmick.csproj -- Samples/EditorDemo/demo.sgv.json --smoke-ui --out /tmp/kg-ui.ppm
dotnet run --project KuroakiGimmick.csproj -- Samples/EditorDemo/demo.sgv.json --smoke-dock-window
KG_DOCK_STRESS_FRAMES=300 dotnet run --project KuroakiGimmick.csproj -- Samples/EditorDemo/demo.sgv.json --smoke-dock-window
```

The native smoke opens each primary Viewer and Editor panel, resizes it, checks pointer isolation and Preview coordinate mapping, edits a detached Inspector field and undoes it, tests main-window mixed tab clicks and single-root splitter resize/cancel, native tab reorder/splitter cancel/cross-window merge, exports an MP4 while windows are open, and checks Window Motion coexistence and window cleanup.

Desktop window creation is disabled on Switch and mobile, leaving their single-window UI available. macOS/Metal has been exercised locally; Windows/D3D12, Linux/Vulkan, Wayland compositor behavior, and physical display unplug/replug still require platform testing. Wayland's global pointer and window placement restrictions make the menu path essential.

Native preview sizing uses SDL [window aspect ratio](https://wiki.libsdl.org/SDL3/SDL_SetWindowAspectRatio). Compositors may decline the aspect-ratio request; rendering then retains the full frame without stretching.
