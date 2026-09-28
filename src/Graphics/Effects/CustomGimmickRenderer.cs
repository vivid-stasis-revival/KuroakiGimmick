using KuroakiGimmick.Core;

namespace KuroakiGimmick.Graphics;

/// <summary>Custom 对象的 star / cover / DF / sides 精灵。纹理按精灵名缓存，失败的名字只报一次诊断，不每帧重试。</summary>
public sealed class CustomGimmickRenderer : IDisposable
{
    readonly Canvas canvas;
    readonly Dictionary<string, Texture> textures = [];
    readonly HashSet<string> failed = [];
    public CustomGimmickRenderer(Canvas canvas) => this.canvas = canvas;
    /// <summary>按精灵名取纹理；路径必须落在 CustomGimmicks 目录内且通过预算检查，分配前先 Flush。失败写入诊断并记名，后续直接返回 null。</summary>
    Texture? Image(Session session, string name)
    {
        if (textures.TryGetValue(name, out var texture)) return texture;
        if (failed.Contains(name)) return null;
        try
        {
            string file = ResourceFiles.ContainedFile(Path.Combine(Paths.Assets, "CustomGimmicks"), name + ".png");
            new ImageResourceBudget().Check(file); canvas.Flush();
            texture = Texture.Load(canvas.Gpu, file); textures.Add(name, texture); return texture;
        }
        catch (Exception ex) when (ResourceFiles.IsResourceError(ex))
        { failed.Add(name); session.Chart.Diagnostics.Add(new("custom-gimmick/" + name, 0, ex.Message, true)); return null; }
    }
    static (float H, float S, float V) Hsv(uint rgb)
    {
        var c = Color.Hex(rgb); float max = Math.Max(c.R, Math.Max(c.G, c.B)), min = Math.Min(c.R, Math.Min(c.G, c.B)), d = max - min;
        float h = d == 0 ? 0 : max == c.R ? (c.G - c.B) / d % 6 : max == c.G ? (c.B - c.R) / d + 2 : (c.R - c.G) / d + 4;
        return ((h / 6 + 1) % 1, max == 0 ? 0 : d / max, max);
    }
    /// <summary>星星颜色按 y 在上下两端色之间插值：色相/饱和度在 HSV 空间线性插值，明度插完后再乘到 RGB 上，与原版一致。mask 通道不染色。</summary>
    public void DrawStars(Session session, double time, bool mask)
    {
        foreach (var star in session.Stars.At(time, mask))
        {
            if (Image(session, "sp_sk_star_" + star.Frame) is not { } image) continue;
            Color color = Color.White;
            if (!mask)
            {
                var a = Hsv(star.Top); var b = Hsv(star.Bottom); float p = (float)(star.Y / 185);
                float L(float x, float y) => x + (y - x) * p;
                color = Color.Hsv(L(a.H, b.H), L(a.S, b.S)); float v = L(a.V, b.V);
                color = new(color.R * v, color.G * v, color.B * v);
            }
            canvas.Quad(image, new((float)star.X - 7, (float)star.Y - 7, 14, 14), color.Alpha(star.Alpha));
        }
    }
    /// <summary>雪花噪点按拍推进帧号，四块 160×90 分屏各错开一帧；cover1 对应 sp_cover4_0，这组映射照抄原版，别按名字顺序“纠正”。</summary>
    public void DrawCovers(Session session, double time)
    {
        if (session.Chart.ObjectName != "obj_custom_gimmick") return;
        double snow = session.Timeline.Get("static", time);
        if (snow > 0)
        {
            int frame = (int)((Math.Floor(session.Timeline.Bpm.Beat(time) * 16) % 4 + 4) % 4);
            var positions = new (float X, float Y)[] { (0, 0), (160, 0), (160, 90), (0, 90) };
            for (int i = 0; i < 4; i++)
                if (Image(session, "sp_static_" + ((frame + i) % 4)) is { } texture)
                    canvas.Quad(texture, new(positions[i].X, positions[i].Y, 160, 90), Color.White.Alpha(snow));
        }
        foreach (var (mod, sprite) in new[] { ("cover2", "sp_cover2_0"), ("cover3", "sp_cover3_0"), ("cover1", "sp_cover4_0") })
        {
            double alpha = session.Timeline.Get(mod, time);
            if (alpha > 0 && Image(session, sprite) is { } image) canvas.Quad(image, new(0, 0, 320, 180), Color.Hex(0).Alpha(alpha));
        }
    }
    public void Dispose() { foreach (var texture in textures.Values) texture.Dispose(); textures.Clear(); }

    /// <summary>DF 装饰：两条侧线、白底和可裁剪的网格。网格按 df_grid_top/bottom 同时裁几何与 UV，拉伸贴图不等价。</summary>
    public void DrawDf(Session session, double time)
    {
        if (!session.DfEnabled) return;
        double M(string name) => session.Timeline.Get(name, time);
        if (M("df_sideline_alpha") > 0 && Image(session, "sp_df_sideline_0") is { } line)
        {
            float left = (float)(-1 + M("df_sideline") * 114), right = (float)(320 - M("df_sideline") * 114);
            canvas.Quad(line, new(left, 0, 1, 180), Color.White.Alpha(M("df_sideline_alpha")));
            canvas.Quad(line, new(right, 0, 1, 180), Color.White.Alpha(M("df_sideline_alpha")));
        }
        if (M("df_whitebg") > 0) canvas.Fill(new(-1, -1, 321, 181), Color.White.Alpha(M("df_whitebg")));
        float top = (float)Math.Clamp(M("df_grid_top"), 0, 180), bottom = (float)Math.Clamp(M("df_grid_bottom"), 0, 180);
        if (bottom > top && M("df_grid_alpha") > 0 && Image(session, "sp_df_grid_0") is { } grid)
            canvas.Quad(grid, new(0, top, 320, bottom - top), Color.White.Alpha(M("df_grid_alpha")), new(0, top / 180, 1, (bottom - top) / 180));
    }
    /// <summary>
    /// 侧边展开演出。Unravel/Astellion 侧条寿命 .3875 秒，Apocalypse 侧条寿命 1/3 秒。
    /// 拖尾粒子每 .0067 秒出生、存活 .6 秒；时间轴给回调留 1 秒，包含侧条消失后的粒子。
    /// 位置与色相全部来自 Timeline.Hash(seed)，同一 e.Index 每次重放结果相同，不使用运行时随机数。
    /// </summary>
    public void DrawSides(Session session, double time)
    {
        if (session.Chart.ObjectName != "obj_custom_gimmick") return;
        foreach (var e in session.Timeline.Callbacks.Where(e => CustomCompatibility.IsSideCallback(e.Name) && e.Time <= time && e.Time + 1 >= time))
        {
            double age = time - e.Time;
            bool apocalypse = e.Name == "apocalypse_sidething", astellion = e.Name == "astellion_sidething";
            // Apocalypse starts opaque, decelerates at 1800 px/s² and fades at 3/s.
            double sideLife = apocalypse ? 1.0 / 3 : .3875;
            double Distance(double t) => 800 * t - (apocalypse ? 900 * t * t : 0);
            foreach (int dir in new[] { 1, -1 })
            {
                double startX = dir == 1 ? 150 : 170;
                // Astellion has no unravel trail; InitSides creates one expanding spark per side.
                if (astellion && age < .25 && Image(session, "sp_ast_particle_0") is { } spark)
                {
                    uint seed = unchecked((uint)e.Index * 47 + (dir == 1 ? 9127u : 19213u));
                    float x = (dir == 1 ? 0 : 200) + MathF.Floor(Timeline.Hash(seed) * 121);
                    float y = MathF.Floor(Timeline.Hash(seed + 1) * 181);
                    float size = 34 * (float)Math.Min(1, age / .125);
                    double alpha = age <= .125 ? 1 : 1 - (age - .125) / .125;
                    canvas.Quad(spark, new(x - size / 2, y - size / 2, size, size), Color.Hsv(Timeline.Hash(seed + 2), 1).Alpha(alpha));
                }
                if (!astellion && Image(session, "sp_particle_0") is { } particle)
                    for (int i = 0; i * .0067 < sideLife && i * .0067 <= age; i++)
                    {
                        double birth = i * .0067, elapsed = age - birth; if (elapsed >= .6) continue;
                        uint seed = unchecked((uint)e.Index * 65537 + (uint)i * 41 + (dir == 1 ? 7301u : 9413u));
                        float x = (float)(startX - Distance(birth) * dir), y = Math.Min(160, MathF.Floor(Timeline.Hash(seed) * 141) + 20) - (float)(elapsed * 30);
                        float size = (float)((.3 + .2 * Timeline.Hash(seed + 1)) * (1 - .5 * elapsed / .6) * 32);
                        float hue = (dir == 1 ? (apocalypse ? 16 : 128) + MathF.Floor(Timeline.Hash(seed + 2) * (apocalypse ? 25 : 43)) : 192 + MathF.Floor(Timeline.Hash(seed + 2) * 33)) / 255;
                        double alpha = .6 * Math.Min(elapsed / .06, (1 - elapsed / .6) / .9);
                        canvas.Quad(particle, new(x - size / 2, y - size / 2, size, size), Color.Hsv(hue, 1).Alpha(alpha), angle: (float)(-540 * elapsed * dir));
                    }
                if (age <= sideLife && Image(session, astellion ? "sp_sidebar4_0" : "sp_sidebar3_0") is { } side)
                    canvas.Quad(side, new((float)(startX - Distance(age) * dir) - 60 * dir, 0, 120 * dir, 180),
                        Color.White.Alpha(apocalypse ? Math.Max(0, 1 - 3 * age) : .4));
            }
        }
    }
}
