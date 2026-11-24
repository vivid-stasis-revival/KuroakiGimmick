// 切换独立的预览和编辑布局，协调其窗口几何、重置与预设。

namespace KuroakiGimmick.Core;

/// <summary>决定使用哪套停靠布局的工作区模式。</summary>
public enum WorkspaceMode
{
    /// <summary>使用预览工作区布局。</summary>
    Viewer,
    /// <summary>使用编辑器工作区布局。</summary>
    Editor
}

/// <summary>分别管理预览与编辑模式的停靠树。歌曲、编辑及播放状态由外层持有；WorkspaceWindowManager 在 SDL 线程上管理窗口句柄。</summary>
public sealed class WorkspaceManager
{
    readonly Dictionary<string, DockWindowFile> viewerWindows = new(StringComparer.Ordinal);
    readonly Dictionary<string, DockWindowFile> editorWindows = new(StringComparer.Ordinal);
    bool migrateViewerGeometry, migrateEditorGeometry;
    /// <summary>观看模式独立持有的布局树。</summary>
    public DockLayout Viewer { get; private set; }
    /// <summary>编辑模式独立持有的布局树。</summary>
    public DockLayout Editor { get; private set; }
    /// <summary>当前激活的工作区模式。</summary>
    public WorkspaceMode Mode { get; private set; }
    /// <summary>当前模式正在使用的布局树。</summary>
    public DockLayout Current => Mode == WorkspaceMode.Editor ? Editor : Viewer;

    /// <summary>从设备偏好恢复独立的观看和编辑布局。</summary>
    /// <param name="preferences">设备本地的工作区设置。</param>
    /// <param name="initial">启动时采用的工作区模式。</param>
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
    /// <summary>在相应模式下记住窗口几何。</summary>
    /// <param name="bounds">应保存的窗口几何。</param>
    public void RememberWindow(DockWindowFile bounds)
        => RememberWindow(Mode, bounds);
    /// <summary>在相应模式下记住窗口几何。</summary>
    /// <param name="mode">所属工作区模式。</param>
    /// <param name="bounds">应保存的窗口几何。</param>
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
    /// <summary>按模式与窗口标识查找已保存的窗口几何。</summary>
    /// <param name="mode">工作区模式。</param>
    /// <param name="id">窗口的稳定键。</param>
    /// <returns>保存的窗口数据，无匹配时为 null。</returns>
    public DockWindowFile? WindowGeometry(WorkspaceMode mode, string id)
    {
        var source = mode == WorkspaceMode.Editor ? editorWindows : viewerWindows;
        return source.TryGetValue(id, out var bounds) ? bounds : null;
    }
    /// <summary>使旧版栏宽设置与默认编辑器停靠树保持等效的几何尺寸。</summary>
    /// <param name="width">目标宽度。</param>
    /// <param name="height">目标高度。</param>
    /// <param name="geometry">编辑模式旧布局的几何。</param>
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
    /// <summary>将旧式观看界面的栏宽同步到默认停靠树。</summary>
    /// <param name="width">目标宽度。</param>
    /// <param name="height">目标高度。</param>
    /// <param name="geometry">观看模式旧布局的几何。</param>
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
    /// <summary>切换当前布局模式。</summary>
    /// <param name="mode">新的工作区模式。</param>
    /// <returns>实际发生模式切换时为 true。</returns>
    public bool Switch(WorkspaceMode mode)
    {
        if (Mode == mode) return false;
        Mode = mode;
        return true;
    }
    /// <summary>重新建立当前模式的默认布局。</summary>
    public void ResetCurrent()
    {
        if (Mode == WorkspaceMode.Editor)
        {
            Editor = DockLayout.Default(true);
            migrateEditorGeometry = true;
        }
        else
        {
            Viewer = DockLayout.Default(false);
            migrateViewerGeometry = true;
        }
        (Mode == WorkspaceMode.Editor ? editorWindows : viewerWindows).Clear();
    }
    /// <summary>重新建立两种模式的默认布局。</summary>
    public void ResetAll()
    {
        Viewer = DockLayout.Default(false);
        Editor = DockLayout.Default(true);
        migrateViewerGeometry = migrateEditorGeometry = true;
        viewerWindows.Clear();
        editorWindows.Clear();
    }
    /// <summary>将保存的布局预设应用到工作区。</summary>
    /// <param name="file">待加载的布局预设。</param>
    /// <exception cref="InvalidDataException">预设的工作区模式或格式版本不匹配。</exception>
    public void LoadPreset(DockLayoutFile file)
    {
        bool editor = Mode == WorkspaceMode.Editor;
        if (file == null || file.Editor != editor || file.Version != 1)
            throw new InvalidDataException("Workspace preset belongs to a different mode or version.");
        var restored = DockLayout.Restore(file, editor);
        if (editor) migrateEditorGeometry = false;
        else migrateViewerGeometry = false;
        if (editor)
        {
            Editor = restored;
            editorWindows.Clear();
            CopyGeometry(file, Editor, editorWindows);
        }
        else
        {
            Viewer = restored;
            viewerWindows.Clear();
            CopyGeometry(file, Viewer, viewerWindows);
        }
    }
    /// <summary>将两种模式的布局及窗口几何写入偏好对象。</summary>
    /// <param name="preferences">接收布局快照的设备偏好。</param>
    public void Save(WorkspaceLayout preferences)
    {
        preferences.SaveDock(false, Viewer, viewerWindows);
        preferences.SaveDock(true, Editor, editorWindows);
    }
}
