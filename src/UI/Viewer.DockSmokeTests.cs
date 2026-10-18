using KuroakiGimmick.Core;
using KuroakiGimmick.Core.Editing;
using KuroakiGimmick.Core.Windows;
using KuroakiGimmick.Graphics;
using KuroakiGimmick.Native;
using System.Runtime.InteropServices;

namespace KuroakiGimmick.UI;

public sealed partial class Viewer
{
    /// <summary>Real SDL windows and one shared GPU device; requires a desktop display and a sample chart.</summary>
    public void SmokeDockNative()
    {
        if (!WorkspaceWindowManager.SupportsDetachedWindows || Current.IsEmpty)
            throw new InvalidOperationException("Dock native smoke requires a desktop sample project.");
        preferences.UiAnimations = false;
        preferences.DockableWorkspaceEnabled = false;
        // Synthetic coordinates below are in a 1440x940 UI. User DPI/zoom/layout
        // preferences must not redirect a splitter click into toolbar buttons.
        preferences.Workspace = new WorkspaceLayout { UiScale = 1, FollowDisplayScale = false };
        ResetDockWorkspaces();
        Sdl.SDL_SetWindowSize(host.Window, 1440, 940);
        Sdl.SDL_Delay(30);
        if (editorMode) ToggleWorkspace();
        Draw(1440, 940); host.Gpu.Submit();
        if (new ViewerSettings().DockableWorkspaceEnabled || workspaceWindows.Windows.Count != 1 ||
            !dockPanelRects.Any(p => p.Panel == "preview" && p.Active))
            throw new InvalidOperationException("Classic workspace must be the default.");
        void ToggleDockSetting()
        {
            settings = true; settingsSection = "workspace";
            Draw(1440, 940); host.Gpu.Submit();
            Handle(new Sdl.Event { Type = 0x401, Button = 1, X = 330, Y = 240 });
            Draw(1440, 940); host.Gpu.Submit();
            Handle(new Sdl.Event { Type = 0x402, Button = 1, X = 330, Y = 240 });
            settings = false;
            Draw(1440, 940); host.Gpu.Submit();
        }
        ToggleDockSetting();
        if (!DockingEnabled) throw new InvalidOperationException("Settings toggle did not enable docking.");
        // Exercise the actual toolbar during Draw, not a direct OpenEditor call.
        for (int round = 0; round < 3; round++)
        {
            Handle(new Sdl.Event { Type = 0x401, Button = 1, X = 1030, Y = 40 });
            Draw(1440, 940); host.Gpu.Submit();
            Handle(new Sdl.Event { Type = 0x402, Button = 1, X = 1030, Y = 40 });
            Draw(1440, 940); host.Gpu.Submit();
            if (!editorMode || editor == null || dockPanelRects.Any(p => !DockPanels.Editor.Contains(p.Panel)))
                throw new InvalidOperationException("Toolbar did not enter a consistent Editor workspace.");
            Handle(new Sdl.Event { Type = 0x401, Button = 1, X = 930, Y = 40 });
            Draw(1440, 940); host.Gpu.Submit();
            Handle(new Sdl.Event { Type = 0x402, Button = 1, X = 930, Y = 40 });
            Draw(1440, 940); host.Gpu.Submit();
            if (editorMode || dockPanelRects.Any(p => !DockPanels.Viewer.Contains(p.Panel)))
                throw new InvalidOperationException("Toolbar did not return to a consistent Viewer workspace.");
        }
        Console.WriteLine("PASS toolbar workspace switching: three Viewer/Editor roundtrips.");
        ResetCurrentWorkspace(); Draw(1440, 940); host.Gpu.Submit();
        if (Environment.GetEnvironmentVariable("KG_DOCK_VIEWER_SCREENSHOT") is { } viewerScreenshot)
            Canvas.SavePpm(UiTarget, viewerScreenshot);
        OpenEditor();
        if (!editorMode || editor == null) throw new InvalidOperationException("Editor did not open.");
        var layout = workspaceManager.Editor;
        void Frame()
        {
            SyncDockWindows();
            Draw(1440, 940); host.Gpu.Submit(); PresentDockWindows();
        }
        Frame();
        var dragHeader = dockPanelRects.First(p => p.Panel == "preview" && p.Active).Rect;
        uint mainId = Sdl.SDL_GetWindowID(host.Window);
        float dragX = dragHeader.X + 36, dragY = dragHeader.Y + 12;
        Handle(new Sdl.Event { Type = 0x401, WindowID = mainId, Button = 1, X = dragX, Y = dragY });
        Handle(new Sdl.Event { Type = 0x400, WindowID = mainId, X = dragX + 60, Y = dragY + 15 });
        if (dockDrag is not { Dragging: true }) throw new InvalidOperationException("Panel title did not start docking drag.");
        Handle(new Sdl.Event { Type = 0x300, WindowID = mainId, Scan = 41 });
        if (dockDrag != null || dockMouseCaptured || !layout.Validate())
            throw new InvalidOperationException("Esc did not release docking drag and mouse capture.");
        var timelineHeader = dockPanelRects.First(p => p.Panel == "timeline" && p.Active).Rect;
        Handle(new Sdl.Event { Type = 0x401, WindowID = mainId, Button = 1,
            X = timelineHeader.X + 35, Y = timelineHeader.Y + 50 });
        Handle(new Sdl.Event { Type = 0x400, WindowID = mainId,
            X = timelineHeader.X + 95, Y = timelineHeader.Y + 60 });
        if (dockDrag is not { Dragging: true })
            throw new InvalidOperationException("Timeline tab did not start docking drag.");
        Handle(new Sdl.Event { Type = 0x300, WindowID = mainId, Scan = 41 });
        if (dockDrag != null || dockMouseCaptured)
            throw new InvalidOperationException("Timeline drag cancellation leaked capture.");
        Console.WriteLine("PASS dock drag: title threshold, Esc cancellation, capture release.");
        // A mixed main-window group must expose both panels through real input.
        layout.Move("timeline", DockLayout.MainWindow, "preview", DockSide.Center);
        Frame();
        var merged = dockPanelRects.Where(p => p.Panel is "preview" or "timeline").ToArray();
        if (merged.Length != 2 || merged[0].Rect != merged[1].Rect || editorTimelineRect.H <= 0)
            throw new InvalidOperationException("Main mixed tab group was not rendered.");
        var mergedRect = merged[0].Rect;
        Handle(new Sdl.Event { Type = 0x401, WindowID = mainId, Button = 1,
            X = mergedRect.X + 25, Y = mergedRect.Y + 14 });
        Handle(new Sdl.Event { Type = 0x402, WindowID = mainId, Button = 1,
            X = mergedRect.X + 25, Y = mergedRect.Y + 14 });
        Frame();
        if (previewFrame.H <= 0 || editorTimelineRect.H != 0)
            throw new InvalidOperationException("Main tab click did not select Preview exclusively.");
        var mainSplitter = dockSplitterRects.First();
        float splitBefore = mainSplitter.Split.Ratio;
        Handle(new Sdl.Event { Type = 0x401, WindowID = mainId, Button = 1,
            X = mainSplitter.Rect.X + 2, Y = mainSplitter.Rect.Y + 2 });
        Handle(new Sdl.Event { Type = 0x400, WindowID = mainId,
            X = mainSplitter.Rect.X + 60, Y = mainSplitter.Rect.Y + 60 });
        Frame();
        if (Math.Abs(mainSplitter.Split.Ratio - splitBefore) < .001f)
            throw new InvalidOperationException("Single-window custom splitter did not resize.");
        Handle(new Sdl.Event { Type = 0x300, WindowID = mainId, Scan = 41 });
        if (Math.Abs(mainSplitter.Split.Ratio - splitBefore) > .001f)
            throw new InvalidOperationException("Main splitter Esc did not restore its ratio.");
        ResetCurrentWorkspace(); layout = workspaceManager.Editor; Frame();
        if (Environment.GetEnvironmentVariable("KG_DOCK_EDITOR_SCREENSHOT") is { } editorScreenshot)
            Canvas.SavePpm(UiTarget, editorScreenshot);
        Console.WriteLine("PASS main dock groups: mixed tabs, click/release selection, single-root resize and Esc.");
        foreach (string id in DockPanels.Editor)
        {
            string root = "smoke-" + id;
            if (!layout.AddWindow(root) || !layout.Move(id, root, null, DockSide.Center))
                throw new InvalidOperationException("Could not detach " + id);
            Frame();
            if (!workspaceWindows.TryGet(DockWindowKey(WorkspaceMode.Editor, root), out var window) || window == null ||
                window.Id == Sdl.SDL_GetWindowID(host.Window))
                throw new InvalidOperationException("Missing independent SDL window for " + id);
            float mainX = mouseX, mainY = mouseY;
            Handle(new Sdl.Event { Type = 0x400, WindowID = window.Id, X = 73, Y = 91 });
            if (mouseX != mainX || mouseY != mainY)
                throw new InvalidOperationException("Auxiliary pointer polluted the main window.");
            if (id == "timeline")
            {
                int beforeScroll = trackScroll;
                Handle(new Sdl.Event { Type = 0x403, WindowID = window.Id, WheelY = -1 });
                if (trackScroll <= beforeScroll)
                    throw new InvalidOperationException("Detached Timeline wheel did not scroll its tracks.");
            }
            if (id == "preview")
            {
                if (!window.CleanPreview || (Sdl.SDL_GetWindowFlags(window.Handle) & Sdl.SDL_WINDOW_BORDERLESS) != 0 ||
                    window.Title.Contains("dock-", StringComparison.Ordinal) || window.Title.Contains("smoke-", StringComparison.Ordinal))
                    throw new InvalidOperationException("Preview must retain native window decorations and a readable title.");

                Handle(new Sdl.Event { Type = 0x300, WindowID = window.Id, Scan = 46, Modifiers = 0x40 });
                if (Math.Abs(window.UiScale - 1.125) > .001)
                    throw new InvalidOperationException("Auxiliary UI scale shortcut was not window-local.");
                Handle(new Sdl.Event { Type = 0x300, WindowID = window.Id, Scan = 39, Modifiers = 0x40 });
                window.UiScale = 1.5; Frame();
                var vp = workspaceWindows.Viewport(window, LayoutOptions);
                using (BindPointer(window))
                {
                    if (previewFrame.X != 0 || previewFrame.Y != 0 ||
                        Math.Abs(previewFrame.W - vp.Width) > .01 || Math.Abs(previewFrame.H - vp.Height) > .01 ||
                        previewImage.X < previewFrame.X || previewImage.Y < previewFrame.Y ||
                        previewImage.X + previewImage.W > previewFrame.X + previewFrame.W + .01 ||
                        previewImage.Y + previewImage.H > previewFrame.Y + previewFrame.H + .01)
                        throw new InvalidOperationException("Detached Preview must fit the full game frame inside the window body.");
                    var context = new PanelContext(window.Key, id, previewFrame, vp,
                        new System.Numerics.Vector2(previewImage.X + previewImage.W / 2,
                            previewImage.Y + previewImage.H / 2), true);
                    var scene = context.SceneMouse(previewImage);
                    if (Math.Abs(scene.X - 160) > .01 || Math.Abs(scene.Y - 90) > .01)
                        throw new InvalidOperationException("Detached Editor Preview scene coordinates drifted at 150% UI scale.");
                }
                window.UiScale = 1;
                Handle(new Sdl.Event { Type = 0x401, WindowID = window.Id, Button = 3, X = 20, Y = 40 });
                Frame();
                using (BindPointer(window))
                    if (dockContextPanel != "preview") throw new InvalidOperationException("Picture-only preview has no context-menu exit.");
                Handle(new Sdl.Event { Type = 0x401, WindowID = window.Id, Button = 1, X = 40, Y = 144 });
                Frame();
                if (window.CleanPreview || (Sdl.SDL_GetWindowFlags(window.Handle) & Sdl.SDL_WINDOW_BORDERLESS) != 0)
                    throw new InvalidOperationException("Show window controls did not restore native decorations.");
                window.ShowPreviewControls = false; Frame();
                Console.WriteLine("PASS picture-only Preview: native frame, readable title, context-menu controls, fitted coordinates.");
            }
            Sdl.SDL_SetWindowSize(window.Handle, 260, 180);
            Sdl.SDL_Delay(16);
            Sdl.SDL_GetWindowSize(window.Handle, out int smallWidth, out int smallHeight);
            var definition = PanelRegistry.Editor[id];
            if (smallWidth < Math.Max(240, definition.MinWidth) || smallHeight < (id == "preview" ? 180 : Math.Max(160, definition.MinHeight)))
                throw new InvalidOperationException("Editor panel native minimum size was not enforced: " + id);
            Frame();
            Sdl.SDL_SetWindowSize(window.Handle, 1000, 560);
            Frame();
            Handle(new Sdl.Event { Type = Sdl.WindowCloseRequested, WindowID = window.Id });
            if (layout.WindowOf(id) != DockLayout.MainWindow || workspaceWindows.TryGet(window.Id, out _))
                throw new InvalidOperationException("Window close did not rehome " + id);
            Console.WriteLine("PASS native dock panel: " + id);
        }
        const string contextRoot = "smoke-context";
        layout.AddWindow(contextRoot); layout.Move("images", contextRoot, null, DockSide.Center);
        Frame();
        workspaceWindows.TryGet(DockWindowKey(WorkspaceMode.Editor, contextRoot), out var contextWindow);
        if (contextWindow == null) throw new InvalidOperationException("Dock context window missing.");
        Handle(new Sdl.Event { Type = 0x401, WindowID = contextWindow.Id, Button = 3, X = 50, Y = 15 });
        Frame();
        using (BindPointer(contextWindow))
            if (dockContextPanel != "images") throw new InvalidOperationException("Panel right-click menu did not open.");
        Handle(new Sdl.Event { Type = 0x401, WindowID = contextWindow.Id, Button = 1, X = 100, Y = 151 });
        Frame();
        if (layout.WindowOf("images") != null || !layout.Hidden.Contains("images"))
            throw new InvalidOperationException("Panel context menu did not hide Images.");
        layout.Restore("images"); Frame();
        Console.WriteLine("PASS panel context menu: hide and restore.");
        const string editRoot = "smoke-inspector-edit";
        layout.AddWindow(editRoot); layout.Move("inspector", editRoot, null, DockSide.Center);
        var clip = editor.Vsm.Clips.First();
        selectedClip = clip.Id; selectedClips.Clear(); imageInspector = textInspector = false;
        long beforeRevision = editor.Revision;
        Frame();
        var inspectorWindow = workspaceWindows.TryGet(DockWindowKey(WorkspaceMode.Editor, editRoot), out var found)
            ? found : null;
        if (inspectorWindow == null) throw new InvalidOperationException("Inspector edit window was not created.");
        Handle(new Sdl.Event { Type = 0x401, WindowID = inspectorWindow.Id, Button = 1, X = 620, Y = 130 });
        Frame();
        if (inlineWindow != inspectorWindow.Handle) throw new InvalidOperationException("Inspector IME bound to the wrong window.");
        string beatValue = VsmDocument.Ui(clip.Beat + .125);
        nint text = Marshal.StringToCoTaskMemUTF8(beatValue);
        try { Handle(new Sdl.Event { Type = 0x303, WindowID = inspectorWindow.Id, TextData = text }); }
        finally { Marshal.FreeCoTaskMem(text); }
        Handle(new Sdl.Event { Type = 0x300, WindowID = inspectorWindow.Id, Scan = 40 });
        if (editor.Revision <= beforeRevision ||
            Math.Abs(editor.Vsm.Clips.First(c => c.Id == clip.Id).Beat - (clip.Beat + .125)) > 1e-6)
            throw new InvalidOperationException("Detached Inspector did not edit the shared document.");
        editor.Undo();
        Handle(new Sdl.Event { Type = Sdl.WindowCloseRequested, WindowID = inspectorWindow.Id });
        Console.WriteLine("PASS native Inspector edit: text input, shared document, undo.");
        const string group = "smoke-group";
        layout.AddWindow(group);
        layout.Move("preview", group, null, DockSide.Center);
        layout.Move("inspector", group, "preview", DockSide.Center);
        layout.Move("timeline", group, "preview", DockSide.Bottom);
        Frame();
        if (!workspaceWindows.TryGet(DockWindowKey(WorkspaceMode.Editor, group), out var grouped) || grouped == null ||
            layout.ActivePanel(group) is not ("preview" or "inspector") || !layout.Validate())
            throw new InvalidOperationException("Grouped native dock window was not created.");
        Sdl.SDL_GetGlobalMouseState(out float tabCursorX, out float tabCursorY);
        try
        {
            if (Sdl.SDL_GetWindowPosition(grouped.Handle, out int tabWindowX, out int tabWindowY) &&
                Sdl.SDL_WarpMouseGlobal(tabWindowX + 50, tabWindowY + 15))
            {
                Sdl.SDL_Delay(20);
                Sdl.SDL_GetGlobalMouseState(out float tabGlobalX, out float tabGlobalY);
                if (Math.Abs(tabGlobalX - (tabWindowX + 50)) < 5 && Math.Abs(tabGlobalY - (tabWindowY + 15)) < 5)
                {
                    Handle(new Sdl.Event { Type = 0x401, WindowID = grouped.Id, Button = 1, X = 200, Y = 15 });
                    Handle(new Sdl.Event { Type = 0x400, WindowID = grouped.Id, X = 50, Y = 15 });
                    Handle(new Sdl.Event { Type = 0x402, WindowID = grouped.Id, Button = 1, X = 50, Y = 15 });
                    if (layout.TabsFor("inspector")?[0] != "inspector")
                        throw new InvalidOperationException("Native tab reorder did not update the dock graph.");
                    Console.WriteLine("PASS native tab reorder.");
                }
            }
        }
        finally { Sdl.SDL_WarpMouseGlobal(tabCursorX, tabCursorY); CancelDockDrag(); }
        var groupedViewport = workspaceWindows.Viewport(grouped, LayoutOptions);
        var groupedRects = new List<DockPanelRect>(); var groupedSplitters = new List<DockSplitterRect>();
        layout.Solve(group, new Rect(0, 0, groupedViewport.Width, groupedViewport.Height), groupedRects, groupedSplitters);
        var splitter = groupedSplitters.First(s => s.Split.Axis == DockAxis.Vertical);
        float ratioBefore = splitter.Split.Ratio;
        Handle(new Sdl.Event { Type = 0x401, WindowID = grouped.Id, Button = 1,
            X = splitter.Rect.X + 16, Y = splitter.Rect.Y + 3 });
        Handle(new Sdl.Event { Type = 0x400, WindowID = grouped.Id,
            X = splitter.Rect.X + 16, Y = splitter.Rect.Y + 38 });
        if (dockSplitDrag == null || Math.Abs(splitter.Split.Ratio - ratioBefore) < .01f)
            throw new InvalidOperationException("Native splitter did not resize.");
        Handle(new Sdl.Event { Type = 0x300, WindowID = grouped.Id, Scan = 41 });
        if (dockSplitDrag != null || dockSplitCaptured || Math.Abs(splitter.Split.Ratio - ratioBefore) > .0001f)
            throw new InvalidOperationException("Splitter Esc did not restore ratio and capture.");
        Console.WriteLine("PASS native splitter: resize, Esc rollback, capture release.");
        using (var motionPreview = new NativeWindowPreview(host.Gpu, host.Window))
        {
            motionPreview.Present(Current, transport.Position,
                [new WindowPose(99, 0, .5, .5, .22, .22, true, 0, "Window Motion smoke", true)],
                Canvas, Renderer);
            if (!motionPreview.HasWindows) throw new InvalidOperationException("Window Motion native preview did not coexist with dock windows.");
        }
        Console.WriteLine("PASS Window Motion native preview alongside dock workspace windows.");
        string exportPath = Path.Combine(Path.GetTempPath(), "kg-dock-export-" + Guid.NewGuid().ToString("N") + ".mp4");
        VideoExport? exportProbe = null;
        try
        {
            exportProbe = new VideoExport(Canvas, Renderer, Current,
                new ExportOptions(exportPath, 0, 1, 1, 320, 180, notes, effects));
            export = exportProbe;
            var deadline = System.Diagnostics.Stopwatch.StartNew();
            while (!exportProbe.Completed && exportProbe.Error == null && deadline.Elapsed < TimeSpan.FromSeconds(20))
            {
                exportProbe.Tick(); Frame(); Sdl.SDL_Delay(5);
            }
            if (!exportProbe.Completed || !File.Exists(exportPath) || new FileInfo(exportPath).Length == 0)
                throw new InvalidOperationException("Video export failed while native dock windows were open: " + exportProbe.Error);
            Console.WriteLine("PASS video export with grouped native dock windows.");
        }
        finally
        {
            export = null;
            exportProbe?.Dispose();
            if (File.Exists(exportPath)) File.Delete(exportPath);
        }
        if (int.TryParse(Environment.GetEnvironmentVariable("KG_DOCK_STRESS_FRAMES"), out int stressFrames))
        {
            stressFrames = Math.Clamp(stressFrames, 1, 2000);
            long beforeBytes = System.Diagnostics.Process.GetCurrentProcess().WorkingSet64;
            for (int index = 0; index < stressFrames; index++)
            {
                if (index % 24 == 0)
                    Sdl.SDL_SetWindowSize(grouped.Handle, index % 48 == 0 ? 1100 : 1000,
                        index % 48 == 0 ? 720 : 650);
                if (index % 17 == 0) layout.Activate(index % 34 == 0 ? "preview" : "inspector");
                Frame();
            }
            long afterBytes = System.Diagnostics.Process.GetCurrentProcess().WorkingSet64;
            Console.WriteLine($"PASS native dock stress: {stressFrames} frames, working set {beforeBytes:N0} → {afterBytes:N0} bytes.");
        }
        ToggleWorkspace(); SyncDockWindows();
        if (editorMode || grouped.Shown) throw new InvalidOperationException("Editor window stayed visible in Viewer mode.");
        ToggleWorkspace(); Frame();
        if (!editorMode || !grouped.Shown) throw new InvalidOperationException("Editor window did not restore after mode switch.");
        Handle(new Sdl.Event { Type = Sdl.WindowCloseRequested, WindowID = grouped.Id });
        if (layout.Roots.Count != 1 || !layout.Validate() || dockWindowIds.Count != 0 ||
            auxiliaryPointers.Count != 0 || workspaceWindows.Windows.Count != 1)
            throw new InvalidOperationException("Grouped window close damaged the layout.");
        const string dragSource = "smoke-drag-source", dragTarget = "smoke-drag-target";
        layout.AddWindow(dragSource); layout.Move("preview", dragSource, null, DockSide.Center);
        layout.AddWindow(dragTarget); layout.Move("inspector", dragTarget, null, DockSide.Center);
        Frame();
        workspaceWindows.TryGet(DockWindowKey(WorkspaceMode.Editor, dragSource), out var sourceWindow);
        workspaceWindows.TryGet(DockWindowKey(WorkspaceMode.Editor, dragTarget), out var targetWindow);
        if (sourceWindow == null || targetWindow == null) throw new InvalidOperationException("Cross-window drag setup failed.");
        sourceWindow.ShowPreviewControls = true; Frame();
        Sdl.SDL_GetGlobalMouseState(out float originalX, out float originalY);
        try
        {
            Sdl.SDL_SetWindowPosition(sourceWindow.Handle, 90, 90);
            Sdl.SDL_SetWindowPosition(targetWindow.Handle, 560, 120);
            Sdl.SDL_GetWindowPosition(targetWindow.Handle, out int tx, out int ty);
            Sdl.SDL_GetWindowSize(targetWindow.Handle, out int tw, out int th);
            float targetX = tx + tw * .5f, targetY = ty + th * .5f;
            bool warp = Sdl.SDL_WarpMouseGlobal(targetX, targetY);
            Sdl.SDL_Delay(20);
            Sdl.SDL_GetGlobalMouseState(out float actualX, out float actualY);
            if (warp && Math.Abs(actualX - targetX) < 5 && Math.Abs(actualY - targetY) < 5)
            {
                Handle(new Sdl.Event { Type = 0x401, WindowID = sourceWindow.Id, Button = 1, X = 50, Y = 15 });
                Handle(new Sdl.Event { Type = 0x400, WindowID = sourceWindow.Id, X = 90, Y = 45 });
                if (dockDrag?.Target is not { Window: dragTarget, Side: DockSide.Center })
                    throw new InvalidOperationException("Cross-window center target was not detected.");
                Handle(new Sdl.Event { Type = 0x402, WindowID = sourceWindow.Id, Button = 1, X = 90, Y = 45 });
                if (layout.WindowOf("preview") != dragTarget || !layout.Validate())
                    throw new InvalidOperationException("Cross-window tab merge failed.");
                Console.WriteLine("PASS cross-window drag: global hit test and center tab merge.");
            }
            else
            {
                // Event routing remains usable when the compositor withholds desktop cursor coordinates.
                Handle(new Sdl.Event { Type = 0x401, WindowID = sourceWindow.Id, Button = 1, X = 50, Y = 15 });
                Handle(new Sdl.Event { Type = 0x400, WindowID = targetWindow.Id, X = 500, Y = 280 });
                Handle(new Sdl.Event { Type = 0x402, WindowID = targetWindow.Id, Button = 1, X = 500, Y = 280 });
                if (layout.WindowOf("preview") != dragTarget || !layout.Validate())
                    throw new InvalidOperationException("Cross-window WindowID fallback did not merge tabs.");
                Console.WriteLine("PASS cross-window drag: WindowID fallback center tab merge.");
            }
        }
        finally { Sdl.SDL_WarpMouseGlobal(originalX, originalY); CancelDockDrag(); }
        Frame();
        if (workspaceWindows.TryGet(DockWindowKey(WorkspaceMode.Editor, dragSource), out var remainingSource) && remainingSource != null)
            Handle(new Sdl.Event { Type = Sdl.WindowCloseRequested, WindowID = remainingSource.Id });
        if (workspaceWindows.TryGet(DockWindowKey(WorkspaceMode.Editor, dragTarget), out var remainingTarget) && remainingTarget != null)
            Handle(new Sdl.Event { Type = Sdl.WindowCloseRequested, WindowID = remainingTarget.Id });
        ToggleWorkspace();
        if (editorMode || workspaceManager.Mode != WorkspaceMode.Viewer)
            throw new InvalidOperationException("Viewer workspace did not activate.");
        var viewerLayout = workspaceManager.Viewer;
        foreach (string id in DockPanels.Viewer)
        {
            string root = "smoke-viewer-" + id;
            if (!viewerLayout.AddWindow(root) || !viewerLayout.Move(id, root, null, DockSide.Center))
                throw new InvalidOperationException("Could not detach Viewer " + id);
            Frame();
            if (!workspaceWindows.TryGet(DockWindowKey(WorkspaceMode.Viewer, root), out var window) || window == null)
                throw new InvalidOperationException("Missing Viewer SDL window for " + id);
            float mainX = mouseX, mainY = mouseY;
            Handle(new Sdl.Event { Type = 0x400, WindowID = window.Id, X = 70, Y = 100 });
            if (mouseX != mainX || mouseY != mainY)
                throw new InvalidOperationException("Viewer window pointer polluted main state.");
            if (id == "diagnostics" && Current.Chart.Diagnostics.Count > 1)
            {
                int beforePage = diagnosticPage;
                Handle(new Sdl.Event { Type = 0x403, WindowID = window.Id, WheelY = -1 });
                if (diagnosticPage <= beforePage)
                {
                    using var scope = BindPointer(window);
                    throw new InvalidOperationException($"Detached Diagnostics wheel affected the wrong panel: page {beforePage}->{diagnosticPage}, active={activePanel}, busy={Busy}, settings={settings}.");
                }
            }
            if (id == "preview")
            {
                Handle(new Sdl.Event { Type = 0x300, WindowID = window.Id, Scan = 46, Modifiers = 0x40 });
                if (Math.Abs(window.UiScale - 1.125) > .001)
                    throw new InvalidOperationException("Viewer auxiliary UI scale shortcut failed.");
                Handle(new Sdl.Event { Type = 0x300, WindowID = window.Id, Scan = 39, Modifiers = 0x40 });
                window.UiScale = 1.5; Frame();
                var vp = workspaceWindows.Viewport(window, LayoutOptions);
                using (BindPointer(window))
                {
                    var context = new PanelContext(window.Key, id, previewFrame, vp,
                        new System.Numerics.Vector2(previewImage.X + previewImage.W / 2,
                            previewImage.Y + previewImage.H / 2), true);
                    var scene = context.SceneMouse(previewImage);
                    if (Math.Abs(scene.X - 160) > .01 || Math.Abs(scene.Y - 90) > .01)
                        throw new InvalidOperationException("Detached Viewer Preview scene coordinates drifted at 150% UI scale.");
                }
                window.UiScale = 1;
            }
            Sdl.SDL_SetWindowSize(window.Handle, 260, 180);
            Sdl.SDL_Delay(16);
            Sdl.SDL_GetWindowSize(window.Handle, out int smallWidth, out int smallHeight);
            var definition = PanelRegistry.Viewer[id];
            if (smallWidth < Math.Max(240, definition.MinWidth) || smallHeight < (id == "preview" ? 180 : Math.Max(160, definition.MinHeight)))
                throw new InvalidOperationException("Viewer panel native minimum size was not enforced: " + id);
            Frame();
            Sdl.SDL_SetWindowSize(window.Handle, 1000, 850);
            Frame();
            Handle(new Sdl.Event { Type = Sdl.WindowCloseRequested, WindowID = window.Id });
            if (viewerLayout.WindowOf(id) != DockLayout.MainWindow || workspaceWindows.TryGet(window.Id, out _))
                throw new InvalidOperationException("Viewer close did not rehome " + id);
            Console.WriteLine("PASS native Viewer panel: " + id);
        }
        if (!viewerLayout.Validate() || dockWindowIds.Count != 0 || auxiliaryPointers.Count != 0 ||
            workspaceWindows.Windows.Count != 1)
            throw new InvalidOperationException("Viewer native window cleanup failed.");
        OpenWorkspacePresetName(true);
        nint presetText = Marshal.StringToCoTaskMemUTF8("DockSmoke");
        try { Handle(new Sdl.Event { Type = 0x303, WindowID = mainId, TextData = presetText }); }
        finally { Marshal.FreeCoTaskMem(presetText); }
        Handle(new Sdl.Event { Type = 0x300, WindowID = mainId, Scan = 40 });
        if (!preferences.WorkspacePresets.ContainsKey("viewer:DockSmoke"))
            throw new InvalidOperationException("Named workspace save failed.");
        viewerLayout.Hide("export");
        OpenWorkspacePresetName(false);
        Handle(new Sdl.Event { Type = 0x300, WindowID = mainId, Scan = 40 });
        if (!workspaceManager.Viewer.IsVisible("export"))
            throw new InvalidOperationException("Named workspace load did not restore Export.");
        string presetPath = Path.Combine(Path.GetTempPath(), "kg-dock-presets-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            preferences.Persist(presetPath);
            if (!ViewerSettings.Load(presetPath).WorkspacePresets.ContainsKey("viewer:DockSmoke"))
                throw new InvalidOperationException("Named workspace preset did not survive settings reload.");
        }
        finally { if (File.Exists(presetPath)) File.Delete(presetPath); }
        Console.WriteLine("PASS named workspace save/load and settings roundtrip.");
        if (Environment.GetEnvironmentVariable("KG_DOCK_LAYOUT_SCREENSHOT") is { } screenshot)
        {
            OpenLayout(); Draw(1440, 940); Canvas.SavePpm(UiTarget, screenshot); CloseLayout();
        }
        const string disabledRoot = "smoke-disabled";
        workspaceManager.Viewer.AddWindow(disabledRoot);
        workspaceManager.Viewer.Move("preview", disabledRoot, null, DockSide.Center);
        Frame();
        string savedGraph = AppJson.Serialize(workspaceManager.Viewer.Snapshot(false), ViewerProject.Json);
        ToggleDockSetting(); Frame();
        if (DockingEnabled || workspaceWindows.Windows.Count != 1 || dockWindowIds.Count != 0 ||
            !dockPanelRects.Any(p => p.Panel == "preview" && p.Active) ||
            AppJson.Serialize(workspaceManager.Viewer.Snapshot(false), ViewerProject.Json) != savedGraph)
            throw new InvalidOperationException("Disabling docking must close native tools, restore classic UI, and preserve the graph.");
        ToggleWorkspace(); Frame();
        if (editorTimelineRect.X != 24 || Math.Abs(editorTimelineRect.W - 1392) > .01)
            throw new InvalidOperationException("Classic Editor timeline must span the workspace.");
        ToggleWorkspace(); Frame(); ToggleDockSetting(); Frame();
        if (!DockingEnabled || workspaceManager.Viewer.WindowOf("preview") != disabledRoot || workspaceWindows.Windows.Count != 2)
            throw new InvalidOperationException("Enabling docking must restore the detached layout.");
        workspaceManager.Viewer.CloseWindow(disabledRoot); Frame();
        Console.WriteLine("PASS workspace opt-in: default off, settings button, live disable, classic Viewer/Editor, layout restored on enable.");
        Console.WriteLine("PASS native dock windows: Viewer and Editor panels, shared GPU, Inspector input/undo, pointer isolation, mode switch, close rehome.");
    }
}
