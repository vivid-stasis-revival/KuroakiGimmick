using System.Numerics;
using KuroakiGimmick.Core;
using KuroakiGimmick.Graphics;
using KuroakiGimmick.Native;

namespace KuroakiGimmick.UI;

public sealed partial class Viewer
{
    DockDragSession? dockDrag;
    WorkspaceWindowManager.Window? dockDragSource;
    WorkspaceMode dockDragMode;
    string? dockDragPanel;
    bool dockMouseCaptured;
    Vector2 dockDragPointer;
    string? dockDropRoot;
    Vector2 dockDropPoint;

    string RootFor(WorkspaceWindowManager.Window window) => window.Main ? DockLayout.MainWindow :
        dockWindowIds.TryGetValue(window.Id, out var identity) ? identity.Root : "";

    bool HandleDockDragInput(Sdl.Event e, WorkspaceWindowManager.Window window)
    {
        if (!DockingEnabled) return false;
        if (!WorkspaceWindowManager.SupportsDetachedWindows) return false;
        if (dockDrag == null)
        {
            if (e.Type != 0x401 || e.Button != 1 || UiOverlayVisible || Busy ||
                editDrag != null || ImageGestureActive || resizingLayout != 0 || prGridHudDragging ||
                !TryDockHeader(window, e.X, e.Y, out string? panelId)) return false;
            dockDrag = new(workspaceManager.Current, panelId!, new(e.X, e.Y));
            dockDragSource = window; dockDragMode = workspaceManager.Mode; dockDragPanel = panelId;
            dockDragPointer = new(e.X, e.Y);
            return false; // 单点误触处理
        }
        if (e.Type == 0x300 && e.Scan == 41 || e.Type == 0x20F)
        { CancelDockDrag(); return true; }
        if (e.Type == 0x400)
        {
            UpdateDockDragTarget(e, window);
            if (!dockDrag.Dragging) return false;
            if (!dockMouseCaptured) dockMouseCaptured = Sdl.SDL_CaptureMouse(true);
            held = click = false;
            return true;
        }
        if (e.Type == 0x402 && e.Button == 1)
        {
            UpdateDockDragTarget(e, window);
            bool wasDragging = dockDrag.Dragging;
            if (!wasDragging && dockDragPanel != null &&
                TryDockHeader(window, e.X, e.Y, out var releasedPanel) && releasedPanel == dockDragPanel)
            {
                workspaceManager.Current.Activate(dockDragPanel);
                SaveSettings();
            }
            bool changed = wasDragging && TryReorderDockTab();
            if (wasDragging && !changed)
                changed = dockDrag.Commit(() => "dock-" + Guid.NewGuid().ToString("N"));
            if (changed) SaveSettings();
            CancelDockDrag();
            return wasDragging;
        }
        return dockDrag.Dragging && e.Type is >= 0x300 and <= 0x403;
    }

    void CancelDockDrag()
    {
        dockDrag?.Cancel(); dockDrag = null; dockDragPanel = null;
        dockDropRoot = null;
        if (dockMouseCaptured) Sdl.SDL_CaptureMouse(false);
        dockMouseCaptured = false;
        if (dockDragSource is { } source)
        {
            using var scope = BindPointer(source);
            held = click = false;
        }
        dockDragSource = null;
    }

    void UpdateDockDragTarget(Sdl.Event e, WorkspaceWindowManager.Window eventWindow)
    {
        if (dockDrag == null || dockDragSource == null) return;
        var layout = dockDragMode == WorkspaceMode.Editor ? workspaceManager.Editor : workspaceManager.Viewer;
        WorkspaceWindowManager.Window? targetWindow = null;
        Vector2 targetPoint = default;
        // Prefer the event's own window when capture is not redirecting events.
        if (eventWindow.Id != dockDragSource.Id && eventWindow.Shown)
        { targetWindow = eventWindow; targetPoint = new(e.X, e.Y); }
        else
        {
            Sdl.SDL_GetGlobalMouseState(out float globalX, out float globalY);
            if (float.IsFinite(globalX) && float.IsFinite(globalY))
                foreach (var candidate in workspaceWindows.Windows.Reverse())
                {
                    if (!candidate.Main && !candidate.Shown ||
                        !candidate.Main && (!dockWindowIds.TryGetValue(candidate.Id, out var identity) || identity.Mode != dockDragMode)) continue;
                    if (!Sdl.SDL_GetWindowPosition(candidate.Handle, out int x, out int y) ||
                        !Sdl.SDL_GetWindowSize(candidate.Handle, out int width, out int height) ||
                        globalX < x || globalY < y || globalX >= x + width || globalY >= y + height) continue;
                    var viewport = workspaceWindows.Viewport(candidate, LayoutOptions);
                    targetWindow = candidate;
                    targetPoint = PanelContext.FromNative(globalX - x, globalY - y, viewport);
                    break;
                }
            // Wayland may not expose global coordinates. The source window still has useful local
            // coordinates, and the menu remains the cross-window fallback.
            if (targetWindow == null && eventWindow.Id == dockDragSource.Id)
            { targetWindow = eventWindow; targetPoint = new(e.X, e.Y); }
        }
        var rects = new List<DockPanelRect>(); var splitters = new List<DockSplitterRect>();
        string? root = targetWindow == null ? null : RootFor(targetWindow);
        if (targetWindow != null && root != null)
        {
            var viewport = workspaceWindows.Viewport(targetWindow, LayoutOptions);
            Rect area = targetWindow.Main
                ? new(24, 91, viewport.Width - 48, viewport.Height - (dockDragMode == WorkspaceMode.Editor ? 152 : 159))
                : new(0, 0, viewport.Width, viewport.Height);
            layout.Solve(root, area, rects, splitters);
        }
        dockDrag.Update(root, targetPoint, rects);
        dockDropRoot = root; dockDropPoint = targetPoint;
        dockDragPointer = eventWindow.Id == dockDragSource.Id ? new(e.X, e.Y) : targetPoint;
    }

    bool TryReorderDockTab()
    {
        if (dockDrag == null || dockDragPanel == null || dockDropRoot != dockDrag.SourceWindow) return false;
        var layout = dockDragMode == WorkspaceMode.Editor ? workspaceManager.Editor : workspaceManager.Viewer;
        var tabs = layout.TabsFor(dockDragPanel);
        if (tabs == null || tabs.Count < 2) return false;
        WorkspaceWindowManager.Window? targetWindow = dockDropRoot == DockLayout.MainWindow
            ? workspaceWindows.Main
            : dockWindowIds.Where(entry => entry.Value.Mode == dockDragMode && entry.Value.Root == dockDropRoot)
                .Select(entry => workspaceWindows.TryGet(entry.Key, out var w) ? w : null).FirstOrDefault(w => w != null);
        if (targetWindow == null) return false;
        var viewport = workspaceWindows.Viewport(targetWindow, LayoutOptions);
        Rect area = targetWindow.Main
            ? new(24, 91, viewport.Width - 48, viewport.Height - (dockDragMode == WorkspaceMode.Editor ? 152 : 159))
            : new(0, 0, viewport.Width, viewport.Height);
        var panels = new List<DockPanelRect>(); var splitters = new List<DockSplitterRect>();
        layout.Solve(dockDropRoot, area, panels, splitters);
        Rect rect = panels.FirstOrDefault(p => p.Panel == dockDragPanel).Rect;
        if (rect.W <= 0 || dockDropPoint.Y < rect.Y || dockDropPoint.Y >= rect.Y + 32 ||
            dockDropPoint.X < rect.X || dockDropPoint.X >= rect.X + rect.W) return false;
        float reserve = targetWindow.Main ? 0 : layout.Roots.Count > 2 ? 184 : 94;
        float tabWidth = Math.Max(40, Math.Min(160, (rect.W - 8 - reserve) / tabs.Count));
        if (dockDropPoint.X >= rect.X + 4 + tabWidth * tabs.Count) return false;
        int index = Math.Clamp((int)((dockDropPoint.X - rect.X - 4) / tabWidth), 0, tabs.Count - 1);
        return layout.Reorder(dockDragPanel, index);
    }

    bool TryDockHeader(WorkspaceWindowManager.Window window, float x, float y, out string? panelId)
    {
        panelId = null;
        if (!window.Main && !dockWindowIds.TryGetValue(window.Id, out _)) return false;
        if (window.CleanPreview)
        {
            if ((Sdl.SDL_GetModState() & 0x0CC0) == 0) return false;
            panelId = "preview";
            return true;
        }
        if (prGridHudRect.Contains(x, y)) return false;
        string root = RootFor(window);
        var layout = workspaceManager.Current;
        var viewport = workspaceWindows.Viewport(window, LayoutOptions);
        Rect area = window.Main
            ? new(24, 91, viewport.Width - 48, viewport.Height - (editorMode ? 152 : 159))
            : new(0, 0, viewport.Width, viewport.Height);
        var panels = new List<DockPanelRect>(); var splitters = new List<DockSplitterRect>();
        layout.Solve(root, area, panels, splitters);
        foreach (var group in panels.GroupBy(p => p.Rect))
        {
            var tabs = group.ToArray(); Rect rect = group.Key;
            if (window.Main && editorMode && OriginalSourceGroup(tabs))
            {
                rect = PanelRegistry.MainContent("tools", rect);
                float sourceTabWidth = (rect.W - 12) / 3;
                for (int index = 0; index < tabs.Length; index++)
                    if (new Rect(rect.X + index * (sourceTabWidth + 6), rect.Y + 5, sourceTabWidth, 27).Contains(x, y))
                    { panelId = tabs[index].Panel; return true; }
                continue;
            }
            if (window.Main && !editorMode && OriginalViewerRightGroup(tabs))
            {
                rect = PanelRegistry.MainContent("inspector", rect);
                float half = (rect.W - 10) / 2;
                if (new Rect(rect.X, rect.Y + 37, half, 31).Contains(x, y))
                { panelId = "inspector"; return true; }
                if (new Rect(rect.X + half + 10, rect.Y + 37, half, 31).Contains(x, y))
                { panelId = "diagnostics"; return true; }
                if (!diagnostics && new Rect(rect.X, rect.Y + 418, rect.W, 20).Contains(x, y))
                { panelId = "export"; return true; }
                continue;
            }
            if (window.Main && tabs.Length == 1)
            {
                string id = tabs[0].Panel;
                rect = PanelRegistry.MainContent(id, rect);
                Rect header = id switch
                {
                    "timeline" => new(rect.X + 4, rect.Y + 40, 82, 24),
                    "transport" => new(rect.X + PreviewHorizontalPadding, rect.Y + 50, 150, 30),
                    "preview" => new(rect.X + PreviewHorizontalPadding, rect.Y + 3, 155, 24),
                    _ => new(rect.X, rect.Y + 3, Math.Min(rect.W, 155), 22)
                };
                if (header.Contains(x, y)) { panelId = id; return true; }
                continue;
            }
            bool canMove = !window.Main && layout.Roots.Count > 2;
            float reserve = window.Main ? 0 : canMove ? 184 : 94;
            float tabWidth = Math.Max(40, Math.Min(160, (rect.W - 8 - reserve) / tabs.Length));
            for (int index = 0; index < tabs.Length; index++)
                if (new Rect(rect.X + 4 + index * tabWidth, rect.Y + 3, tabWidth - 4, 24).Contains(x, y))
                { panelId = tabs[index].Panel; return true; }
        }
        return false;
    }

    void DrawDockOverlay(string root, IReadOnlyList<DockPanelRect> panels)
    {
        if (dockDrag is not { Dragging: true } || dockDragMode != workspaceManager.Mode) return;
        if (dockDrag.Target is { } target && target.Window == root)
        {
            Canvas.Fill(target.Preview, soft.Alpha(.22)); Canvas.Border(target.Preview, red);
            var panelRect = panels.FirstOrDefault(p => p.Panel == target.Panel).Rect;
            if (panelRect.W > 0 && panelRect.H > 0)
            {
                float size = Math.Min(42, Math.Min(panelRect.W, panelRect.H) / 5);
                float centerX = panelRect.X + panelRect.W / 2, centerY = panelRect.Y + panelRect.H / 2;
                foreach (var (side, dx, dy) in new[]
                {
                    (DockSide.Center, 0, 0), (DockSide.Left, -1, 0), (DockSide.Right, 1, 0),
                    (DockSide.Top, 0, -1), (DockSide.Bottom, 0, 1)
                })
                {
                    var zone = new Rect(centerX + dx * (size + 4) - size / 2,
                        centerY + dy * (size + 4) - size / 2, size, size);
                    Canvas.Fill(zone, side == target.Side ? red.Alpha(.75) : panel.Alpha(.8));
                    Canvas.Border(zone, soft);
                }
            }
        }
        if (dockDragSource != null && root == RootFor(dockDragSource) && dockDragPanel != null)
        {
            var ghost = new Rect(dockDragPointer.X + 12, dockDragPointer.Y + 12, 128, 26);
            Canvas.Fill(ghost, panel.Alpha(.72)); Canvas.Border(ghost, soft);
            Text(dockDragPanel.ToUpperInvariant(), ghost.X + 8, ghost.Y + 7, 10, white, true, 112);
        }
    }
}
