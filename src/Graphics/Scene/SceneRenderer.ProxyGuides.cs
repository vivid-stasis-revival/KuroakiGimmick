using System.Numerics;

namespace KuroakiGimmick.Graphics;

public sealed partial class SceneRenderer
{
    Shader? proxyGuideShader;
    Target? proxyInspection;
    int? inspectionProxy;
    public Texture? ProxyInspectionTexture => proxyInspection?.Texture;

    public static bool CanInspectProxy(KuroakiGimmick.Core.Session session) =>
        session.Chart.ObjectName is "obj_base_gimmick" or "obj_custom_gimmick" && session.HasProxyEvents;

    /// <summary>Reuse the complete source composition and FX chain, writing to a separate editor
    /// texture. Never mutate PRA/VSM or the Final texture used by windows and export.</summary>
    public void RenderProxyInspection(KuroakiGimmick.Core.Session session, double time, int target, bool notes, bool effects)
    {
        time = session.SceneTime(time);
        proxyInspection ??= new(canvas.Gpu, RenderWidth, RenderHeight);
        if (proxyInspection.Texture.Width != RenderWidth || proxyInspection.Texture.Height != RenderHeight)
            proxyInspection.Resize(RenderWidth, RenderHeight);
        if (!CanInspectProxy(session) || target < 0 || target >= session.Chart.Proxies || session.Timeline.Get("pra", time, target) <= 0)
        {
            canvas.Begin(proxyInspection, RenderWidth, RenderHeight, 320, 180, new Color(0, 0, 0));
            canvas.Flush(); return;
        }
        inspectionProxy = target;
        try { RenderFrame(session, time, notes, effects, proxyInspection); }
        finally { inspectionProxy = null; }
    }

    // Same UV mask as Assets/Shaders/proxy.frag. The mask is evaluated BEFORE wave sampling.
    // This pass deliberately shades an opaque source rectangle, including empty/hidden proxies,
    // so the author can distinguish the selected region from the notes currently inside it.
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

    /// <summary>Draw into the editor's current target only; never called by Render or video export.</summary>
    public void DrawProxyGuides(ProxyGeometry geometry, ProxyMask mask, Rect viewport, Color visible, Color covered, Rect? region = null)
    {
        if (!geometry.Renderable) return;
        proxyGuideShader ??= new(canvas.Gpu, ProxyVertexSource(true), ProxyGuideFragmentSource);
        canvas.Flush();
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
