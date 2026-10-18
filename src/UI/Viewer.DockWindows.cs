using System.Numerics;
using System.Runtime.InteropServices;
using KuroakiGimmick.Core;
using KuroakiGimmick.Graphics;
using KuroakiGimmick.Native;

namespace KuroakiGimmick.UI;

public sealed partial class Viewer
{
    readonly Dictionary<uint, (WorkspaceMode Mode, string Root)> dockWindowIds = [];
    static string DockWindowKey(WorkspaceMode mode, string root) => mode + ":" + root;
    void CaptureMainWindowForMode(WorkspaceMode mode)
    {
        if (!DockingEnabled) return;
        if (workspaceWindows.TryCapture(workspaceWindows.Main, DockLayout.MainWindow) is not { } bounds) return;
        bounds.UiScale = LayoutOptions.UiScale;
        workspaceManager.RememberWindow(mode, bounds);
    }
    void ApplyMainWindowBounds(WorkspaceMode mode)
    {
        if (!DockingEnabled) return;
        if (workspaceManager.WindowGeometry(mode, DockLayout.MainWindow) is not { } saved) return;
        var restored = DockLayout.RecoverBounds(saved, workspaceWindows.DisplayBounds());
        Sdl.SDL_SetWindowSize(host.Window, (int)restored.W, (int)restored.H);
        Sdl.SDL_SetWindowPosition(host.Window, (int)restored.X, (int)restored.Y);
    }

    bool drawingMainDockPanel;
    // Main and native windows use the same groups, headers and body boundaries.
    void DrawMainDockPanels(int w, int h, int pw, int ph, double time, bool editorWorkspace)
    {
        previewFrame = previewImage = editorSourceRect = editorInspectorRect =
            editorTimelineRect = editorTracksRect = trackLabelGrip = default;
        var layout = editorWorkspace ? workspaceManager.Editor : workspaceManager.Viewer;
        foreach (var group in dockPanelRects.GroupBy(p => p.Rect))
        {
            var tabs = group.ToArray();
            var bounds = group.Key;
            drawingMainDockPanel = true;
            inputPanelBounds = bounds;
            Canvas.LimitClip(bounds);
            try
            {
                // Keep the original main-window chrome. New tab bars are only needed
                // for user-created mixed groups; detached windows always retain tabs.
                if (editorWorkspace && OriginalSourceGroup(tabs))
                {
                    string? selectedSource = tabs.FirstOrDefault(p => p.Active).Panel;
                    imageSources = selectedSource == "images"; textSources = selectedSource == "text";
                    Rect source = DockingEnabled ? PanelRegistry.MainContent("tools", bounds) : bounds;
                    inputPanelBounds = source; Canvas.LimitClip(source);
                    DrawEditorSources(new(source.X, source.Y + 5, source.W, Math.Max(0, source.H - 13)));
                    continue;
                }
                if (!editorWorkspace && OriginalViewerRightGroup(tabs))
                {
                    Rect right = DockingEnabled ? PanelRegistry.MainContent("inspector", bounds) : bounds;
                    inputPanelBounds = right; Canvas.LimitClip(right);
                    DrawViewerRight(right, h, time);
                    continue;
                }
                string? selected = tabs.Length == 1 ? tabs[0].Panel : DrawDockTabs(layout, tabs);
                if (selected == null) continue;
                Rect body = tabs.Length == 1 ? bounds :
                    new(bounds.X, bounds.Y + 30, bounds.W, Math.Max(0, bounds.H - 30));
                if (DockingEnabled) body = PanelRegistry.MainContent(selected, body);
                inputPanelBounds = body;
                Canvas.LimitClip(body);
                if (editorWorkspace) DrawEditorDockPanel(selected, body, w, h, pw, ph, time);
                else DrawViewerDockPanel(selected, body, w, h, pw, ph, time);
            }
            finally { Canvas.LimitClip(null); drawingMainDockPanel = false; }
        }
        inputPanelBounds = new(0, 0, w, h);
    }

    static bool OriginalSourceGroup(IReadOnlyList<DockPanelRect> tabs) => tabs.Count == 3 &&
        tabs.All(p => p.Panel is "tools" or "images" or "text");
    static bool OriginalViewerRightGroup(IReadOnlyList<DockPanelRect> tabs) => tabs.Count == 3 &&
        tabs.All(p => p.Panel is "inspector" or "export" or "diagnostics");

    void DrawDetachedPreview(Rect body, UiViewport viewport, double time, bool editorWorkspace)
    {
        previewFrame = body;
        previewImage = Canvas.PreviewRect(body, viewport.PixelWidth / (float)viewport.Width,
            viewport.PixelHeight / (float)viewport.Height, false);
        Canvas.Fill(body, Color.Hex(0x030204));
        using var unfaded = Canvas.Unfaded();
        if (editorWorkspace && !activeWorkspaceWindow.CleanPreview && textCanvas) DrawTextCanvas(previewImage, time);
        else if (editorWorkspace && !activeWorkspaceWindow.CleanPreview && imageCanvas) DrawImageCanvas(previewImage, time);
        else if (!activeWorkspaceWindow.CleanPreview && desktopPreview) DrawDesktop(body, time);
        else
        {
            Canvas.Quad(Renderer.Final.Texture, previewImage, Color.White);
            if (editorWorkspace && !activeWorkspaceWindow.CleanPreview)
            {
                DrawSceneDirectManipulation(previewImage, time);
                DrawPrCalibrationGrid(previewImage, time);
            }
        }
    }

    string? DrawDockTabs(DockLayout layout, IReadOnlyList<DockPanelRect> group)
    {
        if (group.Count == 0) return null;
        Rect bounds = group[0].Rect;
        string? selected = group.FirstOrDefault(p => p.Active).Panel;
        Canvas.Fill(new(bounds.X, bounds.Y, bounds.W, 30), panel);
        bool canMove = !activeWorkspaceWindow.Main && layout.Roots.Count > 2;
        float reserve = activeWorkspaceWindow.Main ? 0 : canMove ? 184 : 94;
        float tabWidth = Math.Max(40, Math.Min(160, (bounds.W - 8 - reserve) / group.Count));
        for (int index = 0; index < group.Count; index++)
        {
            string id = group[index].Panel;
            var tabRect = new Rect(bounds.X + 4 + index * tabWidth, bounds.Y + 3,
                Math.Max(1, tabWidth - 4), 24);
            if (EButton(id.ToUpperInvariant(), tabRect, active: id == selected, key: "dock-tab:" + id))
            { layout.Activate(id); selected = id; activePanel = id; SaveSettings(); }
        }
        if (canMove && selected != null &&
            EButton("MOVE", new(bounds.X + bounds.W - 178, bounds.Y + 3, 82, 24), key: "dock-move:" + selected))
        {
            moveMenuPanel = moveMenuPanel == selected ? null : selected;
            moveMenuAnchor = bounds;
        }
        if (!activeWorkspaceWindow.Main && selected != null &&
            EButton("MAIN", new(bounds.X + bounds.W - 88, bounds.Y + 3, 82, 24), key: "dock-main:" + selected))
        {
            layout.ReturnToMain(selected);
            SaveSettings();
        }
        return selected;
    }

    void DrawDockMoveMenu(DockLayout layout, string currentRoot)
    {
        if (moveMenuPanel is not { } panelId) return;
        var targets = layout.Roots.Keys.Where(id => id != currentRoot).Order(StringComparer.Ordinal).ToArray();
        if (targets.Length == 0) { moveMenuPanel = null; return; }
        float x = Math.Max(2, moveMenuAnchor.X + moveMenuAnchor.W - 244);
        float y = moveMenuAnchor.Y + 30;
        var menu = new Rect(x, y, 238, targets.Length * 30 + 8);
        Canvas.Fill(menu, panel); Canvas.Border(menu, soft);
        for (int index = 0; index < targets.Length; index++)
        {
            string target = targets[index];
            string label = target == DockLayout.MainWindow ? "MAIN WINDOW" : DockWindowLabel(layout, target);
            if (EButton(label, new(x + 4, y + 4 + index * 30, 230, 26), key: "dock-target:" + target))
            {
                layout.Move(panelId, target, layout.ActivePanel(target), DockSide.Center);
                moveMenuPanel = null; SaveSettings();
            }
        }
    }

    string DockWindowLabel(DockLayout layout, string root) => string.Join(" / ",
        layout.PanelsInWindow(root).Select(id => L.Get(id.ToUpperInvariant())));

    void ConfigureDockWindow(WorkspaceWindowManager.Window window, DockLayout layout, string root)
    {
        var ids = layout.PanelsInWindow(root).ToArray();
        string song = Current.Title.Length > 80 ? Current.Title[..80] + "…" : Current.Title;
        string title = "Kuroaki/Gimmick — " + DockWindowLabel(layout, root);
        if (!Current.IsEmpty) title += " — " + song;
        workspaceWindows.ConfigurePresentation(window, title, ids.Length == 1 && ids[0] == "preview");
    }

    /// <summary>Run on the SDL thread after shared state updates, before presenting any auxiliary UI.</summary>
    void SyncDockWindows()
    {
        if (!WorkspaceWindowManager.SupportsDetachedWindows) return;
        if (!DockingEnabled)
        {
            foreach (var id in dockWindowIds.Keys.ToArray())
                if (workspaceWindows.TryGet(id, out var oldWindow) && oldWindow != null)
                {
                    using var scope = BindPointer(oldWindow);
                    if (inlineWindow == oldWindow.Handle) CloseInline();
                    if (modalWindow == oldWindow.Handle) CloseValue();
                    workspaceWindows.Close(oldWindow); ForgetPointer(oldWindow);
                }
            dockWindowIds.Clear();
            return;
        }
        foreach (var (id, identity) in dockWindowIds.ToArray())
        {
            var layout = identity.Mode == WorkspaceMode.Editor ? workspaceManager.Editor : workspaceManager.Viewer;
            if (!workspaceWindows.TryGet(id, out var window) || window == null) { dockWindowIds.Remove(id); continue; }
            if (!layout.Roots.TryGetValue(identity.Root, out var root) || root.Node == null)
            {
                using var scope = BindPointer(window);
                if (inlineWindow == window.Handle) CloseInline();
                if (modalWindow == window.Handle) CloseValue();
                workspaceWindows.Close(window); ForgetPointer(window); dockWindowIds.Remove(id);
            }
            else if (identity.Mode != workspaceManager.Mode || identity.Mode == WorkspaceMode.Editor && !editorMode)
            {
                using var scope = BindPointer(window);
                if (inlineWindow == window.Handle) CloseInline();
                if (modalWindow == window.Handle) CloseValue();
                held = click = false;
                workspaceWindows.Hide(window);
            }
        }
        if (workspaceManager.Mode == WorkspaceMode.Editor && (!editorMode || editor == null)) return;
        foreach (var root in workspaceManager.Current.Roots.Values)
        {
            if (root.Id == DockLayout.MainWindow || root.Node == null) continue;
            string key = DockWindowKey(workspaceManager.Mode, root.Id);
            if (workspaceWindows.TryGet(key, out var existing) && existing != null)
            { ConfigureDockWindow(existing, workspaceManager.Current, root.Id); continue; }
            var saved = workspaceManager.WindowGeometry(workspaceManager.Mode, root.Id);
            var minimum = workspaceManager.Current.MinimumSize(root.Id);
            int minimumWidth = Math.Max(240, (int)Math.Ceiling(minimum.Width));
            int minimumHeight = Math.Max(160, (int)Math.Ceiling(minimum.Height));
            var window = workspaceWindows.Create(key, "Kuroaki/Gimmick — " + DockWindowLabel(workspaceManager.Current, root.Id), 1000,
                workspaceManager.Mode == WorkspaceMode.Viewer ? 850 : 560,
                saved?.UiScale ?? LayoutOptions.UiScale, saved, minimumWidth, minimumHeight);
            ConfigureDockWindow(window, workspaceManager.Current, root.Id);
            dockWindowIds.Add(window.Id, (workspaceManager.Mode, root.Id));
        }
    }

    void PresentDockWindows()
    {
        if (!DockingEnabled) return;
        if (workspaceManager.Mode == WorkspaceMode.Editor && (!editorMode || editor == null)) return;
        foreach (var (id, identity) in dockWindowIds.ToArray())
        {
            if (identity.Mode != workspaceManager.Mode || !workspaceWindows.TryGet(id, out var window) || window == null) continue;
            workspaceWindows.Present(window, Canvas, LayoutOptions, (current, viewport) =>
            {
                using var scope = BindPointer(current);
                previewFrame = previewImage = editorSourceRect = editorInspectorRect =
                    editorTimelineRect = editorTracksRect = trackLabelGrip = default;
                var panels = new List<DockPanelRect>(); var splitters = new List<DockSplitterRect>();
                var layout = identity.Mode == WorkspaceMode.Editor ? workspaceManager.Editor : workspaceManager.Viewer;
                layout.Solve(identity.Root,
                    new Rect(0, 0, viewport.Width, viewport.Height), panels, splitters);
                foreach (var group in panels.GroupBy(p => p.Rect))
                {
                    var tabs = group.ToArray();
                    inputPanelBounds = group.Key;
                    string? selected = current.CleanPreview ? "preview" : DrawDockTabs(layout, tabs);
                    if (selected == null) continue;
                    Rect bounds = group.Key;
                    Rect body = current.CleanPreview ? bounds : new(bounds.X, bounds.Y + 30, bounds.W, Math.Max(1, bounds.H - 30));
                    inputPanelBounds = body;
                    Canvas.LimitClip(body);
                    try
                    {
                        if (selected == "preview")
                            DrawDetachedPreview(body, viewport, transport.Position, identity.Mode == WorkspaceMode.Editor);
                        else if (identity.Mode == WorkspaceMode.Editor)
                            DrawEditorDockPanel(selected, body, viewport.Width, viewport.Height,
                                viewport.PixelWidth, viewport.PixelHeight, transport.Position);
                        else DrawViewerDockPanel(selected, body, viewport.Width, viewport.Height,
                                viewport.PixelWidth, viewport.PixelHeight, transport.Position);
                    }
                    finally { Canvas.LimitClip(null); }
                }
                DrawDockSplitters(identity.Root, splitters);
                Canvas.Clip(null);
                DrawDockOverlay(identity.Root, panels);
                inputPanelBounds = new Rect(0, 0, viewport.Width, viewport.Height);
                if (identity.Mode == WorkspaceMode.Editor && editorTimelineRect.W > 0 && editorTimelineRect.H > 0)
                    DrawAnimatedTrackHelp(editorTimelineRect,
                        !UiBlockingOverlayVisible && !ImageGestureActive ? hoveredEditTrack : null,
                        viewport.Width, viewport.Height);
                DrawDockMoveMenu(layout, identity.Root);
                if (identity.Mode == WorkspaceMode.Editor && (modalActive || pendingDiscard != null || menuOpen))
                    DrawEditorModals(viewport.Width, viewport.Height);
                DrawDockContextMenu(viewport.Width, viewport.Height);
                click = false;
            });
            if (workspaceWindows.TryCapture(window, identity.Root) is { } bounds)
                workspaceManager.RememberWindow(bounds);
        }
    }

    bool HandleDockWindowEvent(Sdl.Event e, WorkspaceWindowManager.Window window)
    {
        if (!dockWindowIds.TryGetValue(window.Id, out var identity)) return false;
        if (!DockingEnabled) return true;
        using var scope = BindPointer(window);
        if (e.Type == Sdl.WindowCloseRequested)
        {
            if (inlineWindow == window.Handle) CloseInline();
            if (modalWindow == window.Handle) CloseValue();
            if (workspaceWindows.TryCapture(window, identity.Root) is { } bounds) workspaceManager.RememberWindow(bounds);
            (identity.Mode == WorkspaceMode.Editor ? workspaceManager.Editor : workspaceManager.Viewer)
                .CloseWindow(identity.Root);
            workspaceWindows.Close(window); ForgetPointer(window); dockWindowIds.Remove(window.Id);
            SaveSettings(); return true;
        }
        if (identity.Mode != workspaceManager.Mode) return true;
        if ((e.Type is 0x401 or 0x20E) && inlineWindow != 0 && inlineWindow != ActiveUiWindow)
        { AcceptInline(); if (InlineActive) return true; }
        if (e.Type == 0x300 && e.Scan == 41 && moveMenuPanel != null)
        { moveMenuPanel = null; return true; }
        if (e.Type == 0x20F)
        {
            held = click = seeking = editorScrub = false;
            CancelImageGesture(); CancelSceneDirectGesture();
            Sdl.SDL_CaptureMouse(false);
        }
        e = LogicalInput(e);
        if (HandleDockContextInput(e, window)) return true;
        if (e.Type == 0x300 && e.Repeat == 0 && (e.Modifiers & 0x0CC0) != 0 &&
            !modalActive && !InlineActive && !UiOverlayVisible && !Busy &&
            (e.Scan is 46 or 87 or 45 or 86 or 39))
        {
            window.UiScale = e.Scan switch
            {
                46 or 87 => Math.Min(2.5, window.UiScale + .125),
                45 or 86 => Math.Max(.75, window.UiScale - .125),
                _ => 1
            };
            held = click = false;
            if (workspaceWindows.TryCapture(window, identity.Root) is { } bounds)
                workspaceManager.RememberWindow(bounds);
            SaveSettings();
            return true;
        }
        if (HandleDockSplitInput(e, window)) return true;
        if (HandleDockDragInput(e, window)) return true;
        if (e.Type is 0x400 or 0x401 or 0x402)
        {
            mouseX = e.X; mouseY = e.Y;
            if (e.Type == 0x401 && e.Button == 1) { click = held = true; FocusDockPanel(identity.Root, e.X, e.Y); }
            if (e.Type == 0x402 && e.Button == 1) held = false;
        }
        if (e.Type == 0x403) FocusDockPanel(identity.Root, mouseX, mouseY);
        if (identity.Mode == WorkspaceMode.Viewer)
        {
            if (!HandleEditorInput(e)) HandleViewerFallback(e);
            return true;
        }
        if (activePanel == "preview" && !window.CleanPreview)
        {
            if (HandlePrGridGesture(e) || HandleProxySceneGesture(e) || HandleSceneDirectGesture(e) ||
                HandleTextGesture(e) || HandleActiveImageGesture(e)) return true;
        }
        if (activePanel == "timeline" && HandleTrackOrderInput(e)) return true;
        if (activePanel is "tools" or "images" or "text" && e.Type == 0x403)
        {
            if (activePanel == "text") textListScroll = Math.Max(0, textListScroll - (int)Math.Round(e.WheelY));
            else if (activePanel == "images") imageListScroll = Math.Max(0, imageListScroll - (int)Math.Round(e.WheelY));
            else editorSourceScroll = Math.Max(0, editorSourceScroll - e.WheelY * 36);
            return true;
        }
        if (HandleEditorInput(e)) return true;
        if (e.Type == 0x300 && e.Scan == 44 && e.Repeat == 0) transport.SetPlaying(!transport.Playing);
        if (e.Type == 0x1000)
        {
            string? path = Marshal.PtrToStringUTF8(e.DropData);
            if (path != null) LoadPaths([path]);
        }
        return true;
    }

    void FocusDockPanel(string root, float x, float y)
    {
        var viewport = Viewport();
        var panels = new List<DockPanelRect>(); var splitters = new List<DockSplitterRect>();
        Rect area = activeWorkspaceWindow.Main
            ? new(24, 91, viewport.Width - 48, viewport.Height - (editorMode ? 152 : 159))
            : new(0, 0, viewport.Width, viewport.Height);
        if (activeWorkspaceWindow.Main && !DockingEnabled) panels.AddRange(dockPanelRects);
        else workspaceManager.Current.Solve(root, area, panels, splitters);
        activePanel = panels.FirstOrDefault(p => p.Active && p.Rect.Contains(x, y)).Panel;
        activePanelRect = panels.FirstOrDefault(p => p.Active && p.Rect.Contains(x, y)).Rect;
    }
}
