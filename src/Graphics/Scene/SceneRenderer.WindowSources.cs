using KuroakiGimmick.Core;
using KuroakiGimmick.Core.Windows;
namespace KuroakiGimmick.Graphics;

public sealed partial class SceneRenderer
{
    /// <summary>多窗口演出用的可选捕获目标；没有窗口绑定的曲目不分配它。</summary>
    Target? windowField;
    /// <summary>为本帧准备透明捕获目标；分辨率变化时跟随场景一起缩放。</summary>
    void BeginWindowCapture(Session session)
    {
        if ((session.WindowMotion.Bindings?.Count ?? 0) == 0) return;
        windowField ??= new Target(canvas.Gpu, RenderWidth, RenderHeight);
        if (windowField.Texture.Width != RenderWidth) windowField.Resize(RenderWidth, RenderHeight);
        canvas.Begin(windowField, RenderWidth, RenderHeight, 320, 180, new Color(0, 0, 0, 0));
        canvas.Flush();
    }
    /// <summary>把透明轨道还原为直通 alpha 后留作窗口源；与主合成共用 fieldComposite，避免透明度被乘两次。</summary>
    void CaptureWindowField(Session session)
    {
        if ((session.WindowMotion.Bindings?.Count ?? 0) == 0 || windowField == null) return;
        canvas.Begin(windowField, RenderWidth, RenderHeight, 320, 180);
        canvas.Quad(field.Texture, new(0, 0, 320, 180), Color.White, shader: fieldComposite);
        canvas.Flush();
    }
    /// <summary>
    /// 主源取最终合成结果，普通 proxy 绑定沿用既有轨道目标与 shader。
    /// 未登记的 GameMaker surface id 直接返回 false，不拿主源顶替。
    /// </summary>
    public bool DrawWindowSource(Session session, double time, WindowPose pose, Rect rect)
    {
        if (pose.Proxy < 0)
        {
            if (pose.Source != 0) return false;
            canvas.Quad(Final.Texture, rect, Color.White); return true;
        }
        int p = pose.Proxy;
        canvas.Flush(); proxyShader.Float("Time", session.Timeline.Bpm.Beat(time)); proxyShader.Vec2("Texel", 1.0 / 320, 1.0 / 180);
        foreach (var (uniform, mod) in new (string, string)[]
        {
            ("xspd", "shxs"), ("xperiod", "shxp"), ("xamp", "shxa"), ("yspd", "shys"), ("yperiod", "shyp"), ("yamp", "shya"),
            ("ct", "shct"), ("ft", "shft"), ("cb", "shcb"), ("fb", "shfb"), ("cl", "shcl"), ("fl", "shfl"), ("cr", "shcr"), ("fr", "shfr")
        }) proxyShader.Float(uniform, session.Timeline.Get(mod, time, p));
        var uv = new Rect((float)(pose.CropX / 320), (float)(pose.CropY / 180), (float)(pose.CropW / 320), (float)(pose.CropH / 180));
        if (pose.FlipX) uv = new(uv.X + uv.W, uv.Y, -uv.W, uv.H);
        if (pose.FlipY) uv = new(uv.X, uv.Y + uv.H, uv.W, -uv.H);
        canvas.Quad(windowField?.Texture ?? field.Texture, rect, Color.White.Alpha(pose.Alpha), uv, shader: proxyShader); canvas.Flush(); return true;
    }
}
