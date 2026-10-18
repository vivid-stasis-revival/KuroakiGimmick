using KuroakiGimmick.Core;
using KuroakiGimmick.Graphics;

namespace KuroakiGimmick.UI;

public sealed partial class Viewer
{
    bool DockingEnabled => preferences.DockableWorkspaceEnabled && WorkspaceWindowManager.SupportsDetachedWindows;

    void SetDockingEnabled(bool enabled)
    {
        enabled &= WorkspaceWindowManager.SupportsDetachedWindows;
        if (preferences.DockableWorkspaceEnabled == enabled) return;
        if (!enabled) SaveSettings(); // Snapshot native geometry before hiding it.
        CancelDockDrag(); EndDockSplit(true);
        if (resizingLayout != 0) EndLayoutResize();
        preferences.DockableWorkspaceEnabled = enabled;
        held = click = seeking = editorScrub = false;
        dockContextPanel = moveMenuPanel = null;
        SaveSettings();
    }

    // The classic UI uses its original fixed columns and adjustable split, independently
    // of every saved dock root. Disabling docking never destroys a custom workspace.
    void SolveClassicPanels(int w, int h, bool editorWorkspace)
    {
        var g = workspaceGeometry;
        void Add(string id, Rect r, bool active = true) => dockPanelRects.Add(new(DockLayout.MainWindow, id, r, active));
        if (editorWorkspace)
        {
            var source = new Rect(24, 91, g.LeftWidth, g.SplitY - 97);
            string active = textSources ? "text" : imageSources ? "images" : "tools";
            foreach (string id in new[] { "tools", "images", "text" }) Add(id, source, id == active);
            Add("preview", new(g.PreviewX - 14, 91, g.PreviewWidth + 28, g.SplitY - 97));
            Add("inspector", new(g.RightX, 91, g.RightWidth, g.SplitY - 97));
            Add("timeline", new(24, g.SplitY, w - 48, h - g.SplitY - 61));
        }
        else
        {
            Add("sources", new(24, 91, g.LeftWidth, h - 159));
            Add("preview", new(g.PreviewX - 14, 91, g.PreviewWidth + 28, g.SplitY - 97));
            Add("transport", new(g.PreviewX - 14, g.SplitY, g.PreviewWidth + 28, h - g.SplitY - 68));
            var right = new Rect(g.RightX, 91, g.RightWidth, h - 143);
            Add("inspector", right, !diagnostics);
            Add("export", right, false);
            Add("diagnostics", right, diagnostics);
        }
    }
    void DrawClassicLayout(int w, int h)
    {
        if (!LayoutVisible) return;
        using var fade = Canvas.Opacity(layoutAlpha);
        Canvas.Fill(new(0, 0, w, h), Color.Hex(0, .88f));
        Rect r = new(w / 2f - 365, h / 2f - 260, 730, 520);
        Canvas.Fill(r, panel); Canvas.Border(r, soft);
        Text(L.Get("LAYOUT / UI SCALE"), r.X + 26, r.Y + 22, 22, white);
        layoutInput = true;
        try
        {
            Text(L.Get("UI scale"), r.X + 26, r.Y + 80, 15, white);
            if (Button("-", new(r.X + 248, r.Y + 68, 42, 34))) SetUiScale(LayoutOptions.UiScale - .125);
            Text($"{LayoutOptions.UiScale * 100:0.#}%", r.X + 307, r.Y + 79, 16, white, true);
            if (Button("+", new(r.X + 400, r.Y + 68, 42, 34))) SetUiScale(LayoutOptions.UiScale + .125);
            if (Button("100%", new(r.X + 459, r.Y + 68, 95, 34))) SetUiScale(1);
            if (Button("150%", new(r.X + 564, r.Y + 68, 130, 34))) SetUiScale(1.5);
            Text(L.Get("Follow display DPI"), r.X + 26, r.Y + 125, 15, white);
            if (Button(LayoutOptions.FollowDisplayScale ? L.Get("ON") : L.Get("OFF"), new(r.X + 248, r.Y + 114, 194, 32), active: LayoutOptions.FollowDisplayScale))
            { var before = Viewport(); LayoutOptions.FollowDisplayScale = !LayoutOptions.FollowDisplayScale; ReprojectPointer(before); SaveSettings(); }
            var vp = Viewport();
            Text(vp.FitLimited ? L.Get("Zoom limited to keep controls visible. Enlarge the window for more zoom.") : L.Get("Ctrl/Cmd +/-: scale. Ctrl/Cmd+0: reset. Mouse coordinates follow the same scale."),
                r.X + 26, r.Y + 163, 12, vp.FitLimited ? soft : muted, max: 678);
            Text(L.Get("Workspace"), r.X + 26, r.Y + 205, 15, white);
            string[] names = [L.Get("PREVIEW"), L.Get("BALANCED"), L.Get("TIMELINE")];
            for (int i = 0; i < names.Length; i++)
                if (Button(names[i], new(r.X + 248 + i * 150, r.Y + 193, 143, 34)))
                { LayoutOptions.EditorPreviewFraction = new[] { .82, .52, .18 }[i]; LayoutOptions.ViewerPreviewFraction = new[] { .82, .60, .25 }[i]; SaveSettings(); click = false; }
            Text(L.Get("Drag side dividers to resize panels; drag the bar below the preview to resize it."), r.X + 26, r.Y + 246, 13, muted, max: 678);
            Text(L.Get("Double-click a divider to reset it. Esc cancels a resize. Layout is saved per device."), r.X + 26, r.Y + 270, 13, muted, max: 678);
            Text(L.Get("Scene resolution"), r.X + 26, r.Y + 318, 15, white);
            int[] sizes = [320, 640, 1280, 1920];
            for (int i = 0; i < sizes.Length; i++)
                if (Button(sizes[i].ToString(), new(r.X + 248 + i * 112, r.Y + 305, 104, 34), active: Current.Project.RenderWidth == sizes[i]))
                    { Current.Project.RenderWidth = sizes[i]; SaveSettings(); }
            Text(L.Get("Preview panel size and UI scale do not change song geometry or MP4 dimensions."), r.X + 26, r.Y + 360, 13, muted, max: 678);
            Text(L.Get("Scene resolution is separate: higher settings use more GPU memory."), r.X + 26, r.Y + 385, 13, muted, max: 678);
            if (Button(L.Get("RESTORE DEFAULT LAYOUT"), new(r.X + 26, r.Y + 444, 230, 40))) ResetCurrentWorkspace();
            if (Button(L.Get("DONE"), new(r.X + 478, r.Y + 444, 216, 40), primary: true)) CloseLayout();
        }
        finally { layoutInput = false; }
    }
}
