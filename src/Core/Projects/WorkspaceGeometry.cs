using KuroakiGimmick.Graphics;
namespace KuroakiGimmick.Core;

/// <summary>纯几何计算。窗口暂时变小只是限制显示，存下来的面板尺寸原样保留，窗口放大后能恢复。</summary>
public readonly record struct WorkspaceGeometry(float LeftWidth, float RightWidth, float PreviewX,
    float PreviewWidth, float RightX, float SplitY, Rect LeftGrip, Rect RightGrip, Rect HorizontalGrip)
{
    static (double Min, double Max) SplitRange(int height, bool editor) => (editor ? 330 : 355, Math.Max(editor ? 331 : 356, height - 270));
    /// <summary>把像素 y 换算成 0.15..0.85 的分割比例；存比例而不是像素，换窗口尺寸后布局仍保持观感一致。</summary>
    public static double Fraction(double y, int height, bool editor)
    {
        var range = SplitRange(height, editor);
        return Math.Clamp((y - range.Min) / (range.Max - range.Min), .15, .85);
    }
    /// <summary>坐标单位是 <see cref="UiViewport"/> 的逻辑界面单位，不是物理像素，也与 320×180 场景空间无关。</summary>
    public static WorkspaceGeometry Create(int width, int height, bool editor, WorkspaceLayout options)
    {
        options.Normalize();
        float left = options.LeftWidth, right = options.RightWidth;
        // 两侧面板挤占预览区时，按各自超出最小宽度的部分等比回缩，而不是一刀切裁掉某一侧。
        float excess = Math.Max(0, left + right - Math.Max(476, width - 88 - 590));
        float grow = left - 194 + right - 282;
        if (excess > 0 && grow > 0) { left -= excess * (left - 194) / grow; right -= excess * (right - 282) / grow; }
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
