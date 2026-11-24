using KuroakiGimmick.Graphics;

namespace KuroakiGimmick.Core;

/// <summary>只与设备相关的工作区偏好。这里不放任何场景坐标或歌曲数据。</summary>
public sealed class WorkspaceLayout
{
    /// <summary>用户设置的工作区界面缩放倍率。</summary>
    public double UiScale { get; set; } = 1;
    /// <summary>是否将显示器缩放计入工作区布局。</summary>
    public bool FollowDisplayScale { get; set; } = true;
    /// <summary>未设置模式专属左栏宽度时的回退值。</summary>
    public float LeftWidth { get; set; } = 194;
    /// <summary>未设置模式专属右栏宽度时的回退值。</summary>
    public float RightWidth { get; set; } = 282;
    // null 表示旧设置未按模式存储宽度，在当前模式首次调整前沿用公共宽度。
    /// <summary>预览模式专用左栏宽度，null 时使用公共值。</summary>
    public float? ViewerLeftWidth { get; set; }
    /// <summary>预览模式专用右栏宽度，null 时使用公共值。</summary>
    public float? ViewerRightWidth { get; set; }
    /// <summary>编辑模式专用左栏宽度，null 时使用公共值。</summary>
    public float? EditorLeftWidth { get; set; }
    /// <summary>编辑模式专用右栏宽度，null 时使用公共值。</summary>
    public float? EditorRightWidth { get; set; }
    /// <summary>读取指定模式和方向的栏宽，必要时使用公共值。</summary>
    /// <param name="editor">为 true 时使用编辑布局，否则使用预览布局。</param>
    /// <param name="left">为 true 时选择左栏，否则选择右栏。</param>
    /// <returns>按模式及方向选择的栏宽。</returns>
    public float SideWidth(bool editor, bool left) => (editor, left) switch
    {
        (true, true) => EditorLeftWidth ?? LeftWidth,
        (true, false) => EditorRightWidth ?? RightWidth,
        (false, true) => ViewerLeftWidth ?? LeftWidth,
        _ => ViewerRightWidth ?? RightWidth
    };
    /// <summary>更新指定工作区模式的侧栏宽度。</summary>
    /// <param name="editor">为 true 时使用编辑布局，否则使用预览布局。</param>
    /// <param name="left">为 true 时选择左栏，否则选择右栏。</param>
    /// <param name="value">新的栏宽。</param>
    public void SetSideWidth(bool editor, bool left, float value)
    {
        if (left)
        {
            value = float.IsFinite(value) ? Math.Clamp(value, 194, 420) : 194;
            if (editor) ViewerLeftWidth ??= LeftWidth;
            else EditorLeftWidth ??= LeftWidth;
            if (editor) EditorLeftWidth = value;
            else ViewerLeftWidth = value;
            LeftWidth = value; // 旧版读取器仍能看到最后修改的栏宽。
        }
        else
        {
            value = float.IsFinite(value) ? Math.Clamp(value, 282, 480) : 282;
            if (editor) ViewerRightWidth ??= RightWidth;
            else EditorRightWidth ??= RightWidth;
            if (editor) EditorRightWidth = value;
            else ViewerRightWidth = value;
            RightWidth = value;
        }
    }
    /// <summary>时间轴轨道标签区的宽度。</summary>
    public float TrackLabelWidth { get; set; } = 224;
    /// <summary>编辑模式下分配给预览区域的比例。</summary>
    public double EditorPreviewFraction { get; set; } = .52;
    /// <summary>观看模式下分配给预览区域的比例。</summary>
    public double ViewerPreviewFraction { get; set; } = .72;
    /// <summary>仅持久化在设备设置中的停靠树，独立于谱面、VSM 和 VSC。</summary>
    public DockLayoutFile? ViewerDock { get; set; }
    /// <summary>可选的编辑模式停靠布局快照。</summary>
    public DockLayoutFile? EditorDock { get; set; }
    /// <summary>从当前设置恢复指定模式的布局。</summary>
    /// <param name="editor">为 true 时使用编辑布局，否则使用预览布局。</param>
    /// <returns>从保存状态恢复的布局。</returns>
    public DockLayout LoadDock(bool editor) => DockLayout.Restore(editor ? EditorDock : ViewerDock, editor);
    /// <summary>将当前树和窗口几何保存为指定模式的布局快照。</summary>
    /// <param name="editor">为 true 时使用编辑布局，否则使用预览布局。</param>
    /// <param name="layout">待保存的停靠树。</param>
    /// <param name="windows">可选的窗口几何映射。</param>
    /// <exception cref="InvalidOperationException">布局损坏，无法生成持久化数据。</exception>
    public void SaveDock(bool editor, DockLayout layout, IReadOnlyDictionary<string, DockWindowFile>? windows = null)
    {
        if (!layout.Validate()) throw new InvalidOperationException("Cannot save a damaged dock layout.");
        if (editor) EditorDock = layout.Snapshot(true, windows);
        else ViewerDock = layout.Snapshot(false, windows);
    }
    /// <summary>就地夹住所有取值并把 NaN/Inf 换回默认值；反序列化后、参与布局前必须调用，否则非有限值会传染到几何计算。</summary>
    public void Normalize()
    {
        UiScale = double.IsFinite(UiScale) ? Math.Clamp(UiScale, .75, 2.5) : 1;
        LeftWidth = float.IsFinite(LeftWidth) ? Math.Clamp(LeftWidth, 194, 420) : 194;
        RightWidth = float.IsFinite(RightWidth) ? Math.Clamp(RightWidth, 282, 480) : 282;
        if (ViewerLeftWidth is float vl) ViewerLeftWidth = float.IsFinite(vl) ? Math.Clamp(vl, 194, 420) : 194;
        if (ViewerRightWidth is float vr) ViewerRightWidth = float.IsFinite(vr) ? Math.Clamp(vr, 282, 480) : 282;
        if (EditorLeftWidth is float el) EditorLeftWidth = float.IsFinite(el) ? Math.Clamp(el, 194, 420) : 194;
        if (EditorRightWidth is float er) EditorRightWidth = float.IsFinite(er) ? Math.Clamp(er, 282, 480) : 282;
        TrackLabelWidth = float.IsFinite(TrackLabelWidth) ? Math.Clamp(TrackLabelWidth, 180, 500) : 224;
        EditorPreviewFraction = double.IsFinite(EditorPreviewFraction) ? Math.Clamp(EditorPreviewFraction, .15, .85) : .52;
        ViewerPreviewFraction = double.IsFinite(ViewerPreviewFraction) ? Math.Clamp(ViewerPreviewFraction, .15, .85) : .72;
    }
}

