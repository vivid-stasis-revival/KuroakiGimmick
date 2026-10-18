using KuroakiGimmick.Core;
using KuroakiGimmick.Graphics;
using KuroakiGimmick.Native;

namespace KuroakiGimmick.UI;

public sealed partial class Viewer
{
    DockSplitterRect? dockSplitDrag;
    DockLayout? dockSplitLayout;
    WorkspaceWindowManager.Window? dockSplitWindow;
    float dockSplitStartRatio;
    bool dockSplitCaptured;

    bool HandleDockSplitInput(Sdl.Event e, WorkspaceWindowManager.Window window)
    {
        if (!DockingEnabled) return false;
        if (dockSplitDrag is { } dragging)
        {
            if (e.Type == 0x300 && e.Scan == 41 || e.Type == 0x20F)
            { EndDockSplit(true); return true; }
            if (window.Id != dockSplitWindow?.Id) return false;
            if (e.Type == 0x400)
            {
                float span = dragging.Split.Axis == DockAxis.Horizontal
                    ? dragging.Parent.W - 6 : dragging.Parent.H - 6;
                float position = dragging.Split.Axis == DockAxis.Horizontal
                    ? e.X - dragging.Parent.X : e.Y - dragging.Parent.Y;
                dockSplitLayout?.Resize(dragging.Split, position / Math.Max(1, span));
                held = click = false;
                return true;
            }
            if (e.Type == 0x402 && e.Button == 1) { EndDockSplit(false); return true; }
            return e.Type is >= 0x300 and <= 0x403;
        }
        if (e.Type != 0x401 || e.Button != 1 || UiOverlayVisible || Busy ||
            dockDrag != null || resizingLayout != 0 || editDrag != null || ImageGestureActive) return false;
        var layout = workspaceManager.Current;
        var viewport = workspaceWindows.Viewport(window, LayoutOptions);
        Rect area = window.Main
            ? new(24, 91, viewport.Width - 48, viewport.Height - (editorMode ? 152 : 159))
            : new(0, 0, viewport.Width, viewport.Height);
        var panels = new List<DockPanelRect>(); var splitters = new List<DockSplitterRect>();
        layout.Solve(RootFor(window), area, panels, splitters);
        foreach (var splitter in splitters)
        {
            if (!splitter.Rect.Contains(e.X, e.Y)) continue;
            if (e.Clicks >= 2)
            { layout.Resize(splitter.Split, .5f); SaveSettings(); held = click = false; return true; }
            dockSplitDrag = splitter; dockSplitWindow = window;
            dockSplitLayout = layout;
            dockSplitStartRatio = splitter.Split.Ratio;
            dockSplitCaptured = Sdl.SDL_CaptureMouse(true);
            held = click = false;
            return true;
        }
        return false;
    }

    void EndDockSplit(bool cancel)
    {
        if (dockSplitDrag is { } dragging && cancel)
            dockSplitLayout?.Resize(dragging.Split, dockSplitStartRatio);
        dockSplitDrag = null; dockSplitWindow = null; dockSplitLayout = null;
        if (dockSplitCaptured) Sdl.SDL_CaptureMouse(false);
        dockSplitCaptured = false;
        held = click = false;
        if (!cancel) SaveSettings();
    }

    void DrawDockSplitters(string root, IReadOnlyList<DockSplitterRect> splitters)
    {
        foreach (var splitter in splitters)
        {
            if (splitter.Window != root) continue;
            bool hovered = splitter.Rect.Contains(mouseX, mouseY) ||
                dockSplitDrag is { } drag && ReferenceEquals(drag.Split, splitter.Split);
            var r = splitter.Rect;
            Canvas.Fill(r, Theme.PanelAlt);
            // Keep a generous input target without drawing a thick accent frame.
            Rect rule = splitter.Split.Axis == DockAxis.Horizontal
                ? new(r.X + r.W / 2, r.Y, 1, r.H)
                : new(r.X, r.Y + r.H / 2, r.W, 1);
            Canvas.Fill(rule, hovered ? soft : line.Alpha(.5));
        }
    }
}
