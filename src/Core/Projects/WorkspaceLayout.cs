using KuroakiGimmick.Graphics;

namespace KuroakiGimmick.Core;

/// <summary>只与设备相关的工作区偏好。这里不放任何场景坐标或歌曲数据。</summary>
public sealed class WorkspaceLayout
{
    public double UiScale { get; set; } = 1;
    public bool FollowDisplayScale { get; set; } = true;
    public float LeftWidth { get; set; } = 194;
    public float RightWidth { get; set; } = 282;
    // Null means an older settings file: use its shared width until this mode is resized.
    public float? ViewerLeftWidth { get; set; }
    public float? ViewerRightWidth { get; set; }
    public float? EditorLeftWidth { get; set; }
    public float? EditorRightWidth { get; set; }
    public float SideWidth(bool editor, bool left) => (editor, left) switch
    {
        (true, true) => EditorLeftWidth ?? LeftWidth,
        (true, false) => EditorRightWidth ?? RightWidth,
        (false, true) => ViewerLeftWidth ?? LeftWidth,
        _ => ViewerRightWidth ?? RightWidth
    };
    public void SetSideWidth(bool editor, bool left, float value)
    {
        if (left)
        {
            value = float.IsFinite(value) ? Math.Clamp(value, 194, 420) : 194;
            if (editor) ViewerLeftWidth ??= LeftWidth; else EditorLeftWidth ??= LeftWidth;
            if (editor) EditorLeftWidth = value; else ViewerLeftWidth = value;
            LeftWidth = value; // legacy readers continue to see the most recently adjusted width
        }
        else
        {
            value = float.IsFinite(value) ? Math.Clamp(value, 282, 480) : 282;
            if (editor) ViewerRightWidth ??= RightWidth; else EditorRightWidth ??= RightWidth;
            if (editor) EditorRightWidth = value; else ViewerRightWidth = value;
            RightWidth = value;
        }
    }
    public float TrackLabelWidth { get; set; } = 224;
    public double EditorPreviewFraction { get; set; } = .52;
    public double ViewerPreviewFraction { get; set; } = .72;
    /// <summary>Device-local dock trees; never serialized into a chart or VSM/VSC document.</summary>
    public DockLayoutFile? ViewerDock { get; set; }
    public DockLayoutFile? EditorDock { get; set; }
    public DockLayout LoadDock(bool editor) => DockLayout.Restore(editor ? EditorDock : ViewerDock, editor);
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

