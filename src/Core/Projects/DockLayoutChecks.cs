using KuroakiGimmick.Graphics;

namespace KuroakiGimmick.Core;

/// <summary>CPU-only docking checks; runs before SDL, GPU, assets and audio are initialized.</summary>
public static class DockLayoutChecks
{
    public static int Run()
    {
        static void Check(bool condition, string message)
        { if (!condition) throw new InvalidOperationException("Dock layout: " + message); }
        Check(DockLayout.SafeRatio(float.NaN) == .5f && DockLayout.SafeRatio(10) == .9f, "ratio normalization");
        var layout = DockLayout.Default(true);
        Check(layout.Validate(), "default editor layout");
        Check(layout.AddWindow("second"), "new root");
        Check(layout.Move("timeline", "second", null, DockSide.Center), "detach timeline");
        Check(layout.WindowOf("timeline") == "second", "timeline ownership");
        Check(layout.Move("inspector", "second", "timeline", DockSide.Bottom), "cross-root split");
        Check(layout.ActivePanel("second") == "timeline", "window active panel lookup");
        Check(layout.Move("preview", "second", "timeline", DockSide.Center), "cross-root merge");
        Check(layout.MinimumSize("second").Height >= 300 + 300 + 6,
            "native minimum accounts for split children");
        Check(layout.Reorder("preview", 0), "tab reorder");
        Check(layout.Validate(), "after transfers");
        var detachedJson = AppJson.Serialize(layout.Snapshot(true), ViewerProject.Json);
        var detachedCopy = DockLayout.Restore(AppJson.Deserialize<DockLayoutFile>(detachedJson, ViewerProject.Json), true);
        Check(detachedCopy.WindowOf("timeline") == "second" && detachedCopy.Validate(), "detached JSON roundtrip");
        var rects = new List<DockPanelRect>(); var grips = new List<DockSplitterRect>();
        layout.Solve("second", new Rect(0, 0, 900, 700), rects, grips);
        Check(rects.Count == 3 && grips.Count == 1 && rects.All(r => r.Rect.W >= 0 && r.Rect.H >= 0), "geometry");
        var compactRects = new List<DockPanelRect>(); var compactGrips = new List<DockSplitterRect>();
        layout.Solve("second", new Rect(0, 0, 220, 140), compactRects, compactGrips);
        Check(compactRects.Count == 3 && compactGrips.Count == 0 && compactRects.Count(r => r.Active) == 1,
            "small window uses temporary compact tabs");
        Check(layout.Activate("inspector"), "select compact subtree tab");
        compactRects.Clear(); compactGrips.Clear();
        layout.Solve("second", new Rect(0, 0, 220, 140), compactRects, compactGrips);
        Check(compactRects.Single(r => r.Active).Panel == "inspector",
            "compact tab activation can reach a later split child");
        var constrained = DockLayout.Default(true);
        var constrainedRoot = (DockSplit)constrained.Roots[DockLayout.MainWindow].Node!;
        constrained.Resize(constrainedRoot, .1f);
        var constrainedRects = new List<DockPanelRect>(); var constrainedGrips = new List<DockSplitterRect>();
        constrained.Solve(DockLayout.MainWindow, new Rect(0, 0, 1400, 900), constrainedRects, constrainedGrips);
        Check(constrainedRects.First(r => r.Panel == "preview").Rect.H >= PanelRegistry.Editor["preview"].MinHeight &&
            constrainedRects.First(r => r.Panel == "timeline").Rect.H >= PanelRegistry.Editor["timeline"].MinHeight,
            "split ratios respect child minimum sizes");
        Check(layout.Hide("tools") && layout.Restore("tools") && !layout.Hide("preview"),
            "hide and restore policy");
        Check(layout.CloseWindow("second") && layout.Roots.Count == 1 &&
            layout.WindowOf("timeline") == DockLayout.MainWindow, "close window returns panels");
        Check(layout.WindowOf("preview") == DockLayout.MainWindow &&
            layout.WindowOf("inspector") == DockLayout.MainWindow, "grouped close rehomes all panels");
        Check(layout.Validate(), "post-close uniqueness");
        Check(layout.Roots[DockLayout.MainWindow].Node is DockSplit { Axis: DockAxis.Vertical,
            Second: DockTabs restoredTimeline } && restoredTimeline.Panels.Contains("timeline"),
            "closing Timeline restores it across the whole main workspace");
        var hoverRects = new List<DockPanelRect>(); var hoverGrips = new List<DockSplitterRect>();
        layout.Solve(DockLayout.MainWindow, new Rect(0, 0, 1400, 900), hoverRects, hoverGrips);
        var inspector = hoverRects.First(r => r.Panel == "inspector");
        var drag = new DockDragSession(layout, "timeline", new System.Numerics.Vector2(0, 0));
        drag.Update(DockLayout.MainWindow, new System.Numerics.Vector2(inspector.Rect.X + inspector.Rect.W / 2,
            inspector.Rect.Y + inspector.Rect.H / 2), hoverRects);
        Check(drag.Dragging && drag.Target?.Side == DockSide.Center, "dock target center");
        float targetSize = Math.Min(42, Math.Min(inspector.Rect.W, inspector.Rect.H) / 5);
        drag.Update(DockLayout.MainWindow, new System.Numerics.Vector2(
            inspector.Rect.X + inspector.Rect.W / 2 - targetSize - 4,
            inspector.Rect.Y + inspector.Rect.H / 2), hoverRects);
        Check(drag.Target?.Side == DockSide.Left, "visible left dock target selects left split");
        drag.Cancel(); Check(!drag.Dragging && layout.WindowOf("timeline") == DockLayout.MainWindow, "Esc cancellation");
        var detach = new DockDragSession(layout, "timeline", System.Numerics.Vector2.Zero);
        detach.Update(null, new System.Numerics.Vector2(60, 40), hoverRects);
        Check(detach.Commit(() => "third") && layout.WindowOf("timeline") == "third", "drag outside detaches");
        Check(layout.CloseWindow("third"), "detached window close");
        var saved = layout.Snapshot(true);
        var json = AppJson.Serialize(saved, ViewerProject.Json);
        var roundtrip = DockLayout.Restore(AppJson.Deserialize<DockLayoutFile>(json, ViewerProject.Json), true);
        Check(roundtrip.Validate() && roundtrip.WindowOf("timeline") == DockLayout.MainWindow, "JSON roundtrip");
        var preferences = new ViewerSettings();
        Check(!preferences.DockableWorkspaceEnabled, "docking defaults off");
        preferences.DockableWorkspaceEnabled = true;
        var optInCopy = AppJson.Deserialize<ViewerSettings>(AppJson.Serialize(preferences, ViewerProject.Json), ViewerProject.Json)!;
        Check(optInCopy.DockableWorkspaceEnabled, "workspace opt-in persists");
        preferences.Workspace.SaveDock(true, layout);
        var settingsCopy = AppJson.Deserialize<ViewerSettings>(AppJson.Serialize(preferences, ViewerProject.Json), ViewerProject.Json)!;
        Check(settingsCopy.Workspace.LoadDock(true).WindowOf("timeline") == DockLayout.MainWindow &&
            settingsCopy.Workspace.LoadDock(false).WindowOf("sources") == DockLayout.MainWindow,
            "separate editor and viewer preferences");
        var manager = new WorkspaceManager(settingsCopy.Workspace);
        Check(manager.Switch(WorkspaceMode.Editor) && manager.Current.WindowOf("timeline") == DockLayout.MainWindow,
            "mode switch retains editor graph");
        manager.Current.Hide("tools");
        manager.Switch(WorkspaceMode.Viewer);
        Check(manager.Current.WindowOf("sources") == DockLayout.MainWindow, "viewer graph is independent");
        manager.Switch(WorkspaceMode.Editor);
        Check(manager.Current.Hidden.Contains("tools"), "editor graph survives roundtrip");
        var legacy = WorkspaceGeometry.Create(1440, 940, true, new WorkspaceLayout());
        var migratedWidths = new WorkspaceLayout { LeftWidth = 245, RightWidth = 310 };
        migratedWidths.SetSideWidth(true, true, 360);
        migratedWidths.SetSideWidth(true, false, 400);
        Check(migratedWidths.SideWidth(false, true) == 245 && migratedWidths.SideWidth(false, false) == 310 &&
            migratedWidths.SideWidth(true, true) == 360 && migratedWidths.SideWidth(true, false) == 400,
            "legacy widths migrate to independent modes");
        manager.ResetCurrent(); manager.SyncLegacyEditorGeometry(1440, 940, legacy);
        var legacyRects = new List<DockPanelRect>(); var legacyGrips = new List<DockSplitterRect>();
        manager.Editor.Solve(DockLayout.MainWindow, new Rect(24, 91, 1392, 788), legacyRects, legacyGrips);
        var legacySource = legacyRects.First(r => r.Panel == "tools").Rect;
        var legacyPreview = legacyRects.First(r => r.Panel == "preview").Rect;
        var legacyInspector = legacyRects.First(r => r.Panel == "inspector").Rect;
        var legacyTimeline = legacyRects.First(r => r.Panel == "timeline").Rect;
        Check(Math.Abs(PanelRegistry.MainContent("tools", legacySource).W - legacy.LeftWidth) < 2 && Math.Abs(legacyTimeline.Y - legacy.SplitY) < 2 &&
            Math.Abs(legacyPreview.X + 6 - legacy.PreviewX) < 2 &&
            Math.Abs(PanelRegistry.MainContent("inspector", legacyInspector).X - legacy.RightX) < 2,
            "legacy editor geometry parity");
        var mainSplit = (DockSplit)manager.Editor.Roots[DockLayout.MainWindow].Node!;
        manager.Editor.Resize(mainSplit, .65f);
        manager.SyncLegacyEditorGeometry(1440, 940, legacy);
        Check(Math.Abs(mainSplit.Ratio - .65f) < .001f, "legacy geometry cannot overwrite a resized dock tree");
        var viewerLegacy = WorkspaceGeometry.Create(1440, 940, false, new WorkspaceLayout());
        manager.SyncLegacyViewerGeometry(1440, 940, viewerLegacy);
        var viewerRects = new List<DockPanelRect>(); var viewerGrips = new List<DockSplitterRect>();
        manager.Viewer.Solve(DockLayout.MainWindow, new Rect(24, 91, 1392, 781), viewerRects, viewerGrips);
        Check(Math.Abs(PanelRegistry.MainContent("sources", viewerRects.First(r => r.Panel == "sources").Rect).W - viewerLegacy.LeftWidth) < 2 &&
            Math.Abs(PanelRegistry.MainContent("inspector", viewerRects.First(r => r.Panel == "inspector").Rect).X - viewerLegacy.RightX) < 2 &&
            Math.Abs(viewerRects.First(r => r.Panel == "preview").Rect.X + 6 - viewerLegacy.PreviewX) < 2 &&
            Math.Abs(viewerRects.First(r => r.Panel == "transport").Rect.Y - viewerLegacy.SplitY) < 2,
            "legacy viewer geometry parity");
        Check(manager.Editor.AddWindow("aux") && manager.Editor.Move("timeline", "aux", null, DockSide.Center),
            "manager detached root");
        manager.RememberWindow(new DockWindowFile { Id = "aux", X = -950, Y = 120, Width = 1000, Height = 440,
            Display = 17, UiScale = 1.25 });
        var savedWorkspace = new WorkspaceLayout(); manager.Save(savedWorkspace);
        var restoredManager = new WorkspaceManager(savedWorkspace, WorkspaceMode.Editor);
        Check(restoredManager.Editor.WindowOf("timeline") == "aux" &&
            savedWorkspace.EditorDock!.Windows.Single(w => w.Id == "aux").X == -950,
            "window geometry and detached tree persist together");
        var presetSettings = new ViewerSettings();
        presetSettings.WorkspacePresets["editor:dual-monitor"] = savedWorkspace.EditorDock!;
        var presetCopy = AppJson.Deserialize<ViewerSettings>(AppJson.Serialize(presetSettings, ViewerProject.Json), ViewerProject.Json)!;
        var presetManager = new WorkspaceManager(new WorkspaceLayout(), WorkspaceMode.Editor);
        presetManager.LoadPreset(presetCopy.WorkspacePresets["editor:dual-monitor"]);
        Check(presetManager.Editor.WindowOf("timeline") == "aux", "named workspace preset roundtrip");
        saved.Nodes[0].Kind = "split"; saved.Nodes[0].First = 0;
        var cycleRecovered = DockLayout.Restore(saved, true);
        Check(cycleRecovered.Validate() && cycleRecovered.WindowOf("timeline") == DockLayout.MainWindow &&
            cycleRecovered.Roots.Count == 1, "cyclic tree fallback");
        var duplicate = DockLayout.Default(true).Snapshot(true);
        duplicate.Nodes.First(n => n.Kind == "tabs").Panels.Add("preview");
        var duplicateRecovered = DockLayout.Restore(duplicate, true);
        Check(duplicateRecovered.Validate() && duplicateRecovered.WindowOf("preview") == DockLayout.MainWindow &&
            duplicateRecovered.Roots.Count == 1, "duplicate panel fallback");
        var nonFinite = DockLayout.Default(true).Snapshot(true);
        nonFinite.Nodes.First(n => n.Kind == "split").Ratio = float.NaN;
        Check(DockLayout.Restore(nonFinite, true).Validate(), "nonfinite ratio fallback");
        var lost = DockLayout.RecoverBounds(new DockWindowFile { X = 20000, Y = 20000, Width = 900, Height = 600 },
            [new Rect(0, 0, 1280, 800)]);
        Check(lost.X < 1280 && lost.Y < 800 && lost.W == 900, "lost display recovery");
        var compact = UiViewport.CreateAuxiliary(260, 180, 520, 360, 2, 2, new WorkspaceLayout());
        Check(compact.Width < 1180 && compact.Height < 860 && compact.InputScaleX > 0,
            "auxiliary viewport has panel-sized minima");
        var panelContext = new PanelContext("second", "preview", new Rect(20, 30, 400, 300), compact,
            new System.Numerics.Vector2(120, 90), true);
        Check(panelContext.LocalMouse == new System.Numerics.Vector2(100, 60) &&
            panelContext.SceneMouse(new Rect(20, 30, 320, 180)) == new System.Numerics.Vector2(100, 60),
            "panel-local and scene input projection");
        Console.WriteLine("Dock layout checks passed.");
        return 0;
    }
}
