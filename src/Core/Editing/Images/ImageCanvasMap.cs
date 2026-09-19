using System.Numerics;

namespace KuroakiGimmick.Core.Editing;

/// <summary>
/// 绘制、命中测试与拖拽共用的同一套可逆世界/视图映射。世界坐标就是 320×180 逻辑空间，视图坐标是已按 UI DPI 归一化后的值。
/// 三者必须走同一份映射：任何一处自己算一遍缩放，鼠标位置就会和画面对不上。
/// </summary>
public readonly record struct ImageCanvasMap(Vector2 Origin, float Scale)
{
    public Vector2 ToView(Vector2 world) => Origin + world * Scale;
    public Vector2 ToWorld(Vector2 view) => (view - Origin) / Scale;
    /// <summary>按矩形短边等比适配 320×180 再乘缩放倍率，画布中心对准 center。Scale 下限 .001f 防止反算时除零。</summary>
    public static ImageCanvasMap Create(float x, float y, float width, float height, Vector2 center, float zoom) =>
        new(new Vector2(x + width / 2, y + height / 2) - center * (Math.Min(width / 320, height / 180) * zoom),
            Math.Max(.001f, Math.Min(width / 320, height / 180) * zoom));
    /// <summary>命中测试走矩阵逆变换：把点变回以中心为原点的单位方块再比 ±0.5，旋转和镜像因此自动成立。矩阵不可逆（缩放为 0）时判为未命中。</summary>
    public static bool Hit(Matrix3x2 matrix, Vector2 world) => Matrix3x2.Invert(matrix, out var inverse) &&
        Vector2.Transform(world, inverse) is var p && Math.Abs(p.X) <= .5f && Math.Abs(p.Y) <= .5f;
    /// <summary>
    /// 由拖动某个角/边手柄反解出新的 ScaleX/ScaleY。handle 是该手柄在单位方块里的位置（分量为 0 表示这一轴不参与）。
    /// uniform 时把鼠标位移投影到手柄初始方向上求单一倍率，等比缩放不会因为斜着拖而偏向某一轴。
    /// </summary>
    public static ImageEditPose Resize(ImageEditPose original, double width, double height, Vector2 world,
        Vector2 handle, bool uniform)
    {
        // 先把鼠标位置转到图片的本地未旋转坐标系，后面的除法才是在图片自己的轴上做。
        var relative = Vector2.Transform(world - new Vector2((float)original.X, (float)original.Y),
            Matrix3x2.CreateRotation((float)(-original.Rotation * Math.PI / 180)));
        // 缩放锚定中心，与 VSP 的中心轴点一致，镜像（负缩放）也照此处理；改成锚定对角会让位置跟着变，等于顺手改了 imgx/imgy。
        double sx = handle.X == 0 ? original.ScaleX : relative.X / (handle.X * width);
        double sy = handle.Y == 0 ? original.ScaleY : relative.Y / (handle.Y * height);
        if (uniform && handle.X != 0 && handle.Y != 0 && Math.Abs(original.ScaleX * original.ScaleY) > 1e-12)
        {
            var initial = new Vector2((float)(handle.X * width * original.ScaleX), (float)(handle.Y * height * original.ScaleY));
            double factor = Vector2.Dot(relative, initial) / Math.Max(1e-12f, initial.LengthSquared());
            sx = original.ScaleX * factor; sy = original.ScaleY * factor;
        }
        return original with { ScaleX = sx, ScaleY = sy };
    }
}
