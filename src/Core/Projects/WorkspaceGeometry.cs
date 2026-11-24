using KuroakiGimmick.Graphics;
namespace KuroakiGimmick.Core;

/// <summary>纯几何计算。窗口暂时变小只是限制显示，存下来的面板尺寸原样保留，窗口放大后能恢复。</summary>
/// <param name="LeftWidth">左栏宽度。</param>
/// <param name="RightWidth">右栏宽度。</param>
/// <param name="PreviewX">预览区左边界。</param>
/// <param name="PreviewWidth">预览区宽度。</param>
/// <param name="RightX">右栏左边界。</param>
/// <param name="SplitY">上下分割处的纵坐标。</param>
/// <param name="LeftGrip">左侧分隔条命中区。</param>
/// <param name="RightGrip">右侧分隔条命中区。</param>
/// <param name="HorizontalGrip">横向分隔条命中区。</param>
public readonly record struct WorkspaceGeometry(float LeftWidth, float RightWidth, float PreviewX,
    float PreviewWidth, float RightX, float SplitY, Rect LeftGrip, Rect RightGrip, Rect HorizontalGrip)
{
    static (double Min, double Max) SplitRange(int height, bool editor) => (editor ? 330 : 355, Math.Max(editor ? 331 : 356, height - 270));
    /// <summary>把像素 y 换算成 0.15..0.85 的分割比例；存比例而不是像素，换窗口尺寸后布局仍保持观感一致。</summary>
    /// <param name="y">分界线纵坐标。</param>
    /// <param name="height">可用布局高度。</param>
    /// <param name="editor">为 true 时使用编辑布局，否则使用预览布局。</param>
    /// <returns>当前模式下限制到有效范围的分割比例。</returns>
    public static double Fraction(double y, int height, bool editor)
    {
        var range = SplitRange(height, editor);
        return Math.Clamp((y - range.Min) / (range.Max - range.Min), .15, .85);
    }
    /// <summary>坐标单位是 <see cref="UiViewport"/> 的逻辑界面单位，不是物理像素，也与 320×180 场景空间无关。</summary>
    /// <param name="width">目标宽度。</param>
    /// <param name="height">目标高度。</param>
    /// <param name="editor">为 true 时使用编辑布局，否则使用预览布局。</param>
    /// <param name="options">设备本地的工作区布局设置。</param>
    /// <returns>侧栏、预览区及拖动手柄的布局几何。</returns>
    public static WorkspaceGeometry Create(int width, int height, bool editor, WorkspaceLayout options)
    {
        options.Normalize();
        float left = options.SideWidth(editor, true), right = options.SideWidth(editor, false);
        // 两侧面板挤占预览区时，按各自超出最小宽度的部分等比回缩，而不是一刀切裁掉某一侧。
        float excess = Math.Max(0, left + right - Math.Max(476, width - 88 - 590));
        float grow = left - 194 + right - 282;
        if (excess > 0 && grow > 0)
        {
            left -= excess * (left - 194) / grow;
            right -= excess * (right - 282) / grow;
        }
        float px = 44 + left, rx = width - 24 - right, pw = rx - px - 20;
        var range = SplitRange(height, editor);
        double fraction = editor ? options.EditorPreviewFraction : options.ViewerPreviewFraction;
        float split = (float)(range.Min + fraction * (range.Max - range.Min));
        float gripBottom = editor ? split - 14 : height - 68;
        return new(left, right, px, pw, rx, split,
            new(24 + left + 6, 91, 8, Math.Max(1, gripBottom - 91)),
            new(rx - 14, 91, 8, Math.Max(1, gripBottom - 91)),
            new(px, split - 6, pw, 8));
    }
}
