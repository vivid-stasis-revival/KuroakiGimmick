using KuroakiGimmick.Core;

namespace KuroakiGimmick.Graphics;

public sealed partial class CustomGimmickRenderer
{
    Target? fireworkMask;
    Shader? fireworkCutout, fireworkComposite;
    internal const string FireworkCutoutSource = """
        #version 330 core
        in vec2 v_vTexcoord; in vec4 v_vColour;
        uniform sampler2D gm_BaseTexture;
        out vec4 fragColor;
        void main() {
            vec4 c=texture(gm_BaseTexture,v_vTexcoord)*v_vColour;
            if(c.a<=1.0/255.0) discard;
            // R carries monochrome intensity; G carries source coverage. Both
            // channels then receive XOR blending without changing Canvas alpha.
            fragColor=vec4(c.r,c.a,0.0,1.0);
        }
        """;
    internal const string FireworkCompositeSource = """
        #version 330 core
        in vec2 v_vTexcoord; in vec4 v_vColour;
        uniform sampler2D gm_BaseTexture;
        out vec4 fragColor;
        void main() {
            vec2 mask=texture(gm_BaseTexture,v_vTexcoord).rg;
            fragColor=vec4(v_vColour.rgb*mask.r,mask.g*v_vColour.a);
        }
        """;

    public void DrawFireworks(Target scene, Session session, double time)
    {
        if (session.Chart.ObjectName != "obj_custom_gimmick") return;
        var timeline = session.Timeline;
        var visible = timeline.CustomFireworks.Where(f => f.Launch <= time && time < f.Burst + 1.33704)
            .OrderBy(f => time < f.Burst ? 0 : 1).ThenBy(f => time < f.Burst ? f.Launch : f.Burst).ToArray();
        if (visible.Length == 0) return;
        fireworkMask ??= new(canvas.Gpu, 320, 180);
        fireworkCutout ??= new(canvas.Gpu, Canvas.VertexSource, FireworkCutoutSource);
        fireworkComposite ??= new(canvas.Gpu, Canvas.VertexSource, FireworkCompositeSource);
        // All equal-color objects XOR in the same native-resolution mask. Black
        // overlap becomes transparent when composited, rather than a black sprite.
        foreach (var group in visible.GroupBy(f => timeline.CustomFireworkColor(time < f.Burst ? time : f.Burst, time >= f.Burst)))
        {
            canvas.Begin(fireworkMask, 320, 180, 320, 180, new(0, 0, 0, 0));
            canvas.Blend(BlendFactor.InverseDestinationColor, BlendFactor.InverseSourceColor);
            foreach (var f in group)
            {
                if (time < f.Burst)
                {
                    double progress = Math.Clamp((time - f.Launch) / Math.Max(.001, f.FlightEnd - f.Launch), 0, 1);
                    double q = progress * (2 - progress), size = 14 * (2 - q);
                    if (Image(session, "sp_sc_firework_star_0") is { } star)
                        canvas.Quad(star, new((float)(f.X-size/2), (float)(190+(f.Y-190)*q-size/2), (float)size, (float)size),
                            Color.White, angle: (float)(-360*q), shader: fireworkCutout);
                }
                else
                {
                    double age = Math.Max(0, time - f.Burst);
                    for (uint i = 0; i < 30; i++)
                    {
                        uint seed = f.Seed + i * 3;
                        double angle = Timeline.Hash(seed) * Math.PI * 2, speed = 2.8 + 2 * Timeline.Hash(seed + 1);
                        int frame = Math.Min(3, (int)(Timeline.Hash(seed + 2) * 4));
                        double distance = MaskedStarFieldState.FragmentDistance(age, speed);
                        double x = f.X + Math.Cos(angle) * distance;
                        double y = f.BurstY + Math.Sin(angle) * distance + MaskedStarFieldState.FragmentGravity(age);
                        if (Image(session, "sp_sc_firework_particle_" + frame) is { } fragment)
                            canvas.Quad(fragment, new((float)x-4, (float)y-4, 9, 9), Color.White, shader: fireworkCutout);
                    }
                }
            }
            canvas.Flush();
            canvas.Blend(BlendFactor.SourceAlpha, BlendFactor.InverseSourceAlpha);
            canvas.Begin(scene, scene.Texture.Width, scene.Texture.Height, 320, 180);
            // col_convert swaps the GML BGR input; the chart's value is RGB.
            canvas.Quad(fireworkMask.Texture, new(0, 0, 320, 180), Color.Hex((uint)Math.Clamp(group.Key, 0, 16777215)), shader: fireworkComposite);
            canvas.Flush();
        }
    }
}
