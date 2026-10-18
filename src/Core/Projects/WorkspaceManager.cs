namespace KuroakiGimmick.Core;

public enum WorkspaceMode { Viewer, Editor }

/// <summary>
/// Owns the two independent dock graphs. Shared song/editor/transport state is deliberately absent.
/// Window handles belong to WorkspaceWindowManager and are reconciled on the SDL thread.
/// </summary>
public sealed class WorkspaceManager
{
    readonly Dictionary<string, DockWindowFile> viewerWindows = new(StringComparer.Ordinal);
    readonly Dictionary<string, DockWindowFile> editorWindows = new(StringComparer.Ordinal);
    bool migrateViewerGeometry, migrateEditorGeometry;
    public DockLayout Viewer { get; private set; }
    public DockLayout Editor { get; private set; }
    public WorkspaceMode Mode { get; private set; }
    public DockLayout Current => Mode == WorkspaceMode.Editor ? Editor : Viewer;

    public WorkspaceManager(WorkspaceLayout preferences, WorkspaceMode initial = WorkspaceMode.Viewer)
    {
        migrateViewerGeometry = preferences.ViewerDock == null;
        migrateEditorGeometry = preferences.EditorDock == null;
        Viewer = preferences.LoadDock(false);
        Editor = preferences.LoadDock(true);
        CopyGeometry(preferences.ViewerDock, Viewer, viewerWindows);
        CopyGeometry(preferences.EditorDock, Editor, editorWindows);
        Mode = initial;
    }
    static void CopyGeometry(DockLayoutFile? file, DockLayout layout, Dictionary<string, DockWindowFile> into)
    {
        if (file?.Windows == null) return;
        foreach (var window in file.Windows)
            if (window != null && layout.Roots.ContainsKey(window.Id) &&
                window.Width is >= 100 and <= 16000 && window.Height is >= 100 and <= 16000 &&
                Math.Abs((long)window.X) <= 1000000 && Math.Abs((long)window.Y) <= 1000000 &&
                double.IsFinite(window.UiScale) && window.UiScale is >= .5 and <= 4)
                into[window.Id] = window;
    }
    public void RememberWindow(DockWindowFile bounds)
        => RememberWindow(Mode, bounds);
    public void RememberWindow(WorkspaceMode mode, DockWindowFile bounds)
    {
        var layout = mode == WorkspaceMode.Editor ? Editor : Viewer;
        if (bounds == null || !layout.Roots.ContainsKey(bounds.Id) ||
            bounds.Width is < 100 or > 16000 || bounds.Height is < 100 or > 16000 ||
            Math.Abs((long)bounds.X) > 1000000 || Math.Abs((long)bounds.Y) > 1000000 ||
            !double.IsFinite(bounds.UiScale) || bounds.UiScale is < .5 or > 4) return;
        var into = mode == WorkspaceMode.Editor ? editorWindows : viewerWindows;
        into[bounds.Id] = bounds;
    }
    public DockWindowFile? WindowGeometry(WorkspaceMode mode, string id)
    {
        var source = mode == WorkspaceMode.Editor ? editorWindows : viewerWindows;
        return source.TryGetValue(id, out var bounds) ? bounds : null;
    }
    /// <summary>Keep legacy resize preferences and the default editor tree geometrically equivalent.</summary>
    public void SyncLegacyEditorGeometry(int width, int height, WorkspaceGeometry geometry)
    {
        if (!migrateEditorGeometry) return;
        migrateEditorGeometry = false;
        if (Editor.Roots[DockLayout.MainWindow].Node is not DockSplit { Axis: DockAxis.Vertical } vertical ||
            vertical.First is not DockSplit { Axis: DockAxis.Horizontal } side ||
            side.First is not DockTabs left || !left.Panels.Contains("tools") ||
            side.Second is not DockSplit { Axis: DockAxis.Horizontal } middle ||
            middle.First is not DockTabs preview || !preview.Panels.Contains("preview") ||
            middle.Second is not DockTabs inspector || !inspector.Panels.Contains("inspector") ||
            vertical.Second is not DockTabs timeline || !timeline.Panels.Contains("timeline") ||
            Editor.Roots.Count != 1) return;
        const float gap = 6;
        float areaW = width - 48, areaH = height - 152;
        vertical.Ratio = DockLayout.SafeRatio((geometry.SplitY - 91 - gap) / Math.Max(1, areaH - gap));
        side.Ratio = DockLayout.SafeRatio((geometry.LeftWidth + PanelRegistry.SidebarGutter) / Math.Max(1, areaW - gap));
        float centralX = 24 + geometry.LeftWidth + PanelRegistry.SidebarGutter + gap;
        float centralW = areaW - geometry.LeftWidth - PanelRegistry.SidebarGutter - gap;
        middle.Ratio = DockLayout.SafeRatio((geometry.RightX - PanelRegistry.SidebarGutter - centralX - gap) / Math.Max(1, centralW - gap));
    }
    public void SyncLegacyViewerGeometry(int width, int height, WorkspaceGeometry geometry)
    {
        if (!migrateViewerGeometry) return;
        migrateViewerGeometry = false;
        if (Viewer.Roots[DockLayout.MainWindow].Node is not DockSplit { Axis: DockAxis.Horizontal } side ||
            side.First is not DockTabs sources || !sources.Panels.Contains("sources") ||
            side.Second is not DockSplit { Axis: DockAxis.Horizontal } middle ||
            middle.First is not DockSplit { Axis: DockAxis.Vertical } center ||
            center.First is not DockTabs preview || !preview.Panels.Contains("preview") ||
            center.Second is not DockTabs transport || !transport.Panels.Contains("transport") ||
            middle.Second is not DockTabs right || !right.Panels.Contains("inspector") ||
            Viewer.Roots.Count != 1) return;
        const float gap = 6;
        float areaW = width - 48, areaH = height - 159;
        side.Ratio = DockLayout.SafeRatio((geometry.LeftWidth + PanelRegistry.SidebarGutter) / Math.Max(1, areaW - gap));
        float centralX = 24 + geometry.LeftWidth + PanelRegistry.SidebarGutter + gap;
        float centralW = areaW - geometry.LeftWidth - PanelRegistry.SidebarGutter - gap;
        middle.Ratio = DockLayout.SafeRatio((geometry.RightX - PanelRegistry.SidebarGutter - centralX - gap) / Math.Max(1, centralW - gap));
        center.Ratio = DockLayout.SafeRatio((geometry.SplitY - 91 - gap) / Math.Max(1, areaH - gap));
    }
    public bool Switch(WorkspaceMode mode)
    {
        if (Mode == mode) return false;
        Mode = mode; return true;
    }
    public void ResetCurrent()
    {
        if (Mode == WorkspaceMode.Editor) { Editor = DockLayout.Default(true); migrateEditorGeometry = true; }
        else { Viewer = DockLayout.Default(false); migrateViewerGeometry = true; }
        (Mode == WorkspaceMode.Editor ? editorWindows : viewerWindows).Clear();
    }
    public void ResetAll()
    {
        Viewer = DockLayout.Default(false);
        Editor = DockLayout.Default(true);
        migrateViewerGeometry = migrateEditorGeometry = true;
        viewerWindows.Clear(); editorWindows.Clear();
    }
    public void LoadPreset(DockLayoutFile file)
    {
        bool editor = Mode == WorkspaceMode.Editor;
        if (file == null || file.Editor != editor || file.Version != 1)
            throw new InvalidDataException("Workspace preset belongs to a different mode or version.");
        var restored = DockLayout.Restore(file, editor);
        if (editor) migrateEditorGeometry = false; else migrateViewerGeometry = false;
        if (editor) { Editor = restored; editorWindows.Clear(); CopyGeometry(file, Editor, editorWindows); }
        else { Viewer = restored; viewerWindows.Clear(); CopyGeometry(file, Viewer, viewerWindows); }
    }
    public void Save(WorkspaceLayout preferences)
    {
        preferences.SaveDock(false, Viewer, viewerWindows);
        preferences.SaveDock(true, Editor, editorWindows);
    }
}
