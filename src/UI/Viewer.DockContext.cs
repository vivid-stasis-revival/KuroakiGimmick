using KuroakiGimmick.Core;
using KuroakiGimmick.Graphics;
using KuroakiGimmick.Native;

namespace KuroakiGimmick.UI;

public sealed partial class Viewer
{
    bool HandleDockContextInput(Sdl.Event e, WorkspaceWindowManager.Window window)
    {
        if (!DockingEnabled) return false;
        if (dockContextPanel != null)
        {
            if (e.Type == 0x20F || e.Type == 0x300 && e.Scan == 41)
            { dockContextPanel = null; held = click = false; return true; }
            if (e.Type is 0x400 or 0x401 or 0x402)
            {
                mouseX = e.X; mouseY = e.Y;
                if (e.Type == 0x401 && e.Button == 1) click = held = true;
                if (e.Type == 0x402 && e.Button == 1) held = false;
                if (e.Type == 0x401 && e.Button == 3) dockContextPanel = null;
                return true;
            }
            if (e.Type == 0x403)
            { dockContextScroll = Math.Max(0, dockContextScroll - (int)Math.Round(e.WheelY)); return true; }
            return e.Type is >= 0x300 and <= 0x403;
        }
        if (window.CleanPreview && e.Type == 0x401 && e.Button == 3 && !UiOverlayVisible &&
            !Busy && dockDrag == null && dockSplitDrag == null && editDrag == null && !ImageGestureActive)
        {
            dockContextPanel = "preview";
            dockContextX = e.X; dockContextY = e.Y; dockContextScroll = 0;
            held = click = false;
            return true;
        }
        if (e.Type != 0x401 || e.Button != 3 || UiOverlayVisible || Busy ||
            dockDrag != null || dockSplitDrag != null || editDrag != null || ImageGestureActive ||
            !TryDockHeader(window, e.X, e.Y, out var panelId)) return false;
        dockContextPanel = panelId;
        dockContextX = e.X; dockContextY = e.Y; dockContextScroll = 0;
        held = click = false;
        return true;
    }

    void DrawDockContextMenu(int width, int height)
    {
        if (dockContextPanel is not { } panelId) return;
        var layout = workspaceManager.Current;
        string? sourceRoot = layout.WindowOf(panelId);
        if (sourceRoot == null) { dockContextPanel = null; return; }
        var actions = new List<(string Label, Action Run)>();
        actions.Add(("DETACH TO NEW WINDOW", () =>
        {
            string root = "dock-" + Guid.NewGuid().ToString("N");
            if (layout.AddWindow(root)) layout.Move(panelId, root, null, DockSide.Center);
        }));
        if (sourceRoot != DockLayout.MainWindow)
            actions.Add(("DOCK TO MAIN WINDOW", () => layout.ReturnToMain(panelId)));
        foreach (string other in layout.Roots.Keys.Where(id => id != sourceRoot).Order(StringComparer.Ordinal))
        {
            string target = other;
            actions.Add((target == DockLayout.MainWindow ? "MOVE TO MAIN" : "MOVE TO " + DockWindowLabel(layout, target),
                () => layout.Move(panelId, target, layout.ActivePanel(target), DockSide.Center)));
        }
        if (!activeWorkspaceWindow.Main && panelId == "preview" && layout.PanelsInWindow(sourceRoot).Count() == 1)
        {
            actions.Add((activeWorkspaceWindow.CleanPreview ? "SHOW WINDOW CONTROLS" : "PICTURE ONLY", () =>
            {
                activeWorkspaceWindow.ShowPreviewControls = !activeWorkspaceWindow.ShowPreviewControls;
                ConfigureDockWindow(activeWorkspaceWindow, layout, sourceRoot);
            }));
        }
        actions.Add(("RESET PANEL POSITION", () => layout.ReturnToMain(panelId)));
        var definitions = editorMode ? PanelRegistry.Editor : PanelRegistry.Viewer;
        if (definitions.TryGetValue(panelId, out var definition) && definition.CanHide)
            actions.Add(("CLOSE / HIDE PANEL", () => layout.Hide(panelId)));
        const float rowHeight = 30, menuWidth = 244;
        float menuTop = activeWorkspaceWindow.CleanPreview ? 32 : 2;
        int visible = Math.Min(actions.Count, Math.Max(1, (int)((height - menuTop - 10) / rowHeight)));
        dockContextScroll = Math.Clamp(dockContextScroll, 0, Math.Max(0, actions.Count - visible));
        float x = Math.Clamp(dockContextX, 2, Math.Max(2, width - menuWidth - 2));
        // Keep the menu out of the invisible native move/resize strip.
        float y = Math.Clamp(dockContextY, menuTop, Math.Max(menuTop, height - visible * rowHeight - 8));
        var bounds = new Rect(x, y, menuWidth, visible * rowHeight + 8);
        Canvas.Fill(bounds, panel); Canvas.Border(bounds, soft);
        dockContextInput = true;
        try
        {
            for (int row = 0; row < visible; row++)
            {
                int index = dockContextScroll + row;
                if (!Button(actions[index].Label, new(x + 4, y + 4 + row * rowHeight,
                    menuWidth - 8, rowHeight - 4), key: "dock-menu:" + index)) continue;
                dockContextPanel = null;
                actions[index].Run();
                SaveSettings();
                click = false;
                break;
            }
        }
        finally { dockContextInput = false; }
        if (actions.Count > visible)
            Text($"{dockContextScroll + 1}–{dockContextScroll + visible} / {actions.Count}",
                x + menuWidth - 80, y - 16, 10, soft, true);
    }
}
