using KuroakiGimmick.Graphics;

namespace KuroakiGimmick.Core;

/// <summary>只与设备相关的工作区偏好。这里不放任何场景坐标或歌曲数据。</summary>
public sealed class WorkspaceLayout
{
    public double UiScale { get; set; } = 1;
    public bool FollowDisplayScale { get; set; } = true;
    public float LeftWidth { get; set; } = 194;
    public float RightWidth { get; set; } = 282;
    public float TrackLabelWidth { get; set; } = 224;
    public double EditorPreviewFraction { get; set; } = .52;
    public double ViewerPreviewFraction { get; set; } = .72;
    /// <summary>就地夹住所有取值并把 NaN/Inf 换回默认值；反序列化后、参与布局前必须调用，否则非有限值会传染到几何计算。</summary>
    public void Normalize()
    {
        UiScale = double.IsFinite(UiScale) ? Math.Clamp(UiScale, .75, 2.5) : 1;
        LeftWidth = float.IsFinite(LeftWidth) ? Math.Clamp(LeftWidth, 194, 420) : 194;
        RightWidth = float.IsFinite(RightWidth) ? Math.Clamp(RightWidth, 282, 480) : 282;
        TrackLabelWidth = float.IsFinite(TrackLabelWidth) ? Math.Clamp(TrackLabelWidth, 180, 500) : 224;
        EditorPreviewFraction = double.IsFinite(EditorPreviewFraction) ? Math.Clamp(EditorPreviewFraction, .15, .85) : .52;
        ViewerPreviewFraction = double.IsFinite(ViewerPreviewFraction) ? Math.Clamp(ViewerPreviewFraction, .15, .85) : .72;
    }
}

