// 在检查视图中绘制指定 proxy 与参考线。

using System.Numerics;

namespace KuroakiGimmick.Graphics;

public sealed partial class SceneRenderer
{
    Shader proxyGuideShader;
    Target? proxyInspection;
    int? inspectionProxy;
    /// <summary>检查视图的独立纹理，尚未创建时为 null。</summary>
    public Texture? ProxyInspectionTexture => proxyInspection?.Texture;

    /// <summary>检查当前会话是否支持生成独立的 proxy 检查画面。</summary>
    /// <param name="session">提供谱面、时间轴及已加载资源的会话。</param>
    /// <returns>该会话允许独立检查 proxy 时为 true。</returns>
    public static bool CanInspectProxy(KuroakiGimmick.Core.Session session) =>
        session.Chart.ObjectName is "obj_base_gimmick" or "obj_custom_gimmick" && session.HasProxyEvents;

    /// <summary>复用完整场景合成与 FX 链，将检查结果写入独立的编辑器纹理，保留 PRA、VSM 以及用于窗口和导出的 Final 纹理。</summary>
    /// <param name="session">提供谱面、时间轴及已加载资源的会话。</param>
    /// <param name="time">采样时刻，单位为秒。</param>
    /// <param name="target">目标 proxy 的编号。</param>
    /// <param name="notes">是否包含音符绘制。</param>
    /// <param name="effects">是否启用场景效果。</param>
    public void RenderProxyInspection(KuroakiGimmick.Core.Session session, double time, int target, bool notes, bool effects)
    {
        time = session.SceneTime(time);
        proxyInspection ??= new(canvas.Gpu, RenderWidth, RenderHeight);
        if (proxyInspection.Texture.Width != RenderWidth || proxyInspection.Texture.Height != RenderHeight)
            proxyInspection.Resize(RenderWidth, RenderHeight);
        if (!CanInspectProxy(session) || target < 0 || target >= session.Chart.Proxies || session.Timeline.Get("pra", time, target) <= 0)
        {
            canvas.Begin(proxyInspection, RenderWidth, RenderHeight, 320, 180, new Color(0, 0, 0));
            canvas.Flush();
            return;
        }
        inspectionProxy = target;
        try
        {
            RenderFrame(session, time, notes, effects, proxyInspection);
        }
        finally
        {
            inspectionProxy = null;
        }
    }

    // 与 Assets/Shaders/proxy.frag 共用波形采样前的 UV 遮罩。检查视图用不透明源矩形标记选区，包括空或隐藏 proxy，以便区分选区和内部音符。
    internal const string ProxyGuideFragmentSource = """
        #version 330 core
        in vec2 v_vTexcoord;
        in vec4 v_vColour;
        uniform sampler2D gm_BaseTexture;
        uniform float cl; uniform float cr; uniform float ct; uniform float cb;
        uniform float fl; uniform float fr; uniform float ft; uniform float fb;
        uniform vec4 coveredColor;
        out vec4 fragColor;
        void main() {
            vec2 uv = v_vTexcoord;
            float a = clamp((uv.x-cl)/max(fl,0.0001),0.0,1.0)
                    * clamp((1.0-cr-uv.x)/max(fr,0.0001),0.0,1.0)
                    * clamp((uv.y-ct)/max(ft,0.0001),0.0,1.0)
                    * clamp((1.0-cb-uv.y)/max(fb,0.0001),0.0,1.0);
            float stripe = step(0.5, fract((uv.x*320.0 + uv.y*180.0)/6.0));
            vec4 covered = vec4(coveredColor.rgb, coveredColor.a * mix(0.4,1.0,stripe));
            fragColor = mix(covered, v_vColour, a) * texture(gm_BaseTexture, vec2(0.5));
        }
        """;

    /// <summary>仅绘制到编辑器当前目标，常规场景渲染与视频导出不会调用。</summary>
    /// <param name="geometry">当前 proxy 的几何状态。</param>
    /// <param name="mask">四边裁剪与渐隐状态。</param>
    /// <param name="viewport">参考线的绘制区域。</param>
    /// <param name="visible">可见部分使用的颜色。</param>
    /// <param name="covered">被遮罩部分使用的颜色。</param>
    /// <param name="region">可选的参考区域。</param>
    public void DrawProxyGuides(ProxyGeometry geometry, ProxyMask mask, Rect viewport, Color visible, Color covered, Rect? region = null)
    {
        if (!geometry.Renderable) return;
        geometry.ApplyProjection(proxyGuideShader);
        mask.Apply(proxyGuideShader);
        proxyGuideShader.Vec4("guideViewport", new(viewport.X, viewport.Y, viewport.W / 320, viewport.H / 180));
        proxyGuideShader.Vec4("coveredColor", covered);
        var source = region ?? new Rect(geometry.Left, geometry.Top, geometry.Right - geometry.Left, geometry.Bottom - geometry.Top);
        canvas.Polygon(canvas.White, new Vector2(source.X, source.Y), new Vector2(source.X + source.W, source.Y),
            new Vector2(source.X, source.Y + source.H), new Vector2(source.X + source.W, source.Y + source.H), visible,
            new(source.X / 320, source.Y / 180, source.W / 320, source.H / 180), proxyGuideShader);
        canvas.Flush();
    }
}
