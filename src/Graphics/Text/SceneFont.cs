using System.Numerics;
using System.Text.Json;
using System.Text.RegularExpressions;
using KuroakiGimmick.Core;

namespace KuroakiGimmick.Graphics;

/// <summary>
/// 场景歌词/文字绘制。与交互界面字体分离，确保导出场景不会混入工具面板文字。
/// </summary>
public sealed class SceneFont : IDisposable
{
    /// <summary>page 是 pages[] 的下标，只对内置图集有效；回退到原版图集时整套字形都在同一张纹理上。</summary>
    public sealed class Glyph
    {
        public int page { get; set; }
        public int x { get; set; }
        public int y { get; set; }
        public int w { get; set; }
        public int h { get; set; }
        public float advance { get; set; }
        public float offset { get; set; }
    }

    readonly Texture[] pages;
    readonly Dictionary<string, Glyph> glyphs;
    Dictionary<string, Glyph>? originalGlyphs;
    Texture? originalAtlas;
    string? originalKey;
    float fontX = 1, fontY = 1, fontHeight = 10, ascenderOffset;
    public SceneFont(GpuDevice gpu)
    {
        string root = Path.Combine(Paths.Assets, "SceneFont");
        pages = Directory.GetFiles(root, "page-*.png").Order().Select(p => Texture.Load(gpu, p)).ToArray();
        glyphs = AppJson.Deserialize<Dictionary<string, Glyph>>(File.ReadAllText(Path.Combine(root, "glyphs.json")))!;
    }

    /// <summary>原版图集一旦就绪就整体取代内置图集，不做逐字形混用；缺字退到 '?'，再缺退到空字形（只推进 0 宽度）。</summary>
    Glyph GlyphFor(string s)
    {
        var map = originalGlyphs ?? glyphs;
        return map.TryGetValue(s, out var g) ? g : map.GetValueOrDefault("?") ?? new Glyph();
    }

    /// <summary>
    /// 以 manifest + 字体名为键切换原版图集。键不变时必须直接返回；
    /// 键变化时先 Flush 交出仍引用旧纹理的批次再 Dispose，并把所有派生度量一起复位，
    /// 否则会残留上一套字体的 ScaleX/LineHeight。资源包不可用时键为 null，退回内置图集。
    /// </summary>
    void UseFont(Canvas canvas, Session session)
    {
        var pack = session.GameUi;
        string name = pack.Data?.Fonts.ContainsKey(session.Project.GameUiFont) == true ? session.Project.GameUiFont : "fnt_monacovs";
        string? key = pack.Ready? pack.Manifest + "|" + name : null;
        if (key == originalKey)
        {
            return;
        }
        canvas.Flush();
        originalAtlas?.Dispose();
        originalAtlas = null;
        originalGlyphs = null;
        originalKey = key;
        fontX = fontY = 1;
        fontHeight = 10;
        ascenderOffset = 0;
        if (key == null)
        {
            return;
        }
        var font = pack.Data!.Fonts[name];
        originalAtlas = Texture.Load(canvas.Gpu, pack.File(font.File));
        originalGlyphs = font.Glyphs.Where(g => System.Text.Rune.IsValid(g.Character))
            .GroupBy(g => g.Character).ToDictionary(g => char.ConvertFromUtf32(g.Key), values =>
        {
            var g = values.First();
            return new Glyph
            {
                x = g.X,
                y = g.Y + (int) font.AscenderOffset,
                w = g.W,
                h = g.H,
                advance = g.Advance * font.ScaleX,
                offset = g.Offset
            };
        });
        fontX = font.ScaleX;
        fontY = font.ScaleY;
        fontHeight = font.LineHeight * fontY;
        ascenderOffset = 0;
    }

    float Width(string s) => s.EnumerateRunes().Sum(r => GlyphFor(r.ToString()).advance);
    // 复刻 GameMaker 的 draw_text_ext：只在空格处断行；maxwidth 的单位是字号，缩放之前生效。
    List<string> Wrap(string text, double maxWidth)
    {
        var rows = new List<string>();
        foreach (string paragraph in text.Replace("\r", "").Replace("\t", "    ").Split('\n'))
        {
            string row = "";
            foreach (string token in Regex.Split(paragraph, "(?<= )"))
            {
                if (maxWidth > 0 && row.Length > 0 && Width(row + token.TrimEnd()) > maxWidth)
                {
                    rows.Add(row.TrimEnd());
                    row = "";
                }
                row += token;
            }
            rows.Add(row.TrimEnd());
        }
        return rows;
    }

    /// <summary>
    /// 按拍取每条文本轨道的当前内容并绘制。legacy 轨道（Id 为空）沿用旧工程的语义：不换行、
    /// 行距取字体高度、水平居中、垂直顶对齐，并且颜色走 textcolhex。其余轨道使用 draw_text_ext 的
    /// 三向对齐与 textsep。values 为 null 时从时间轴取值；bounds 回调用于编辑器命中框，
    /// 给出的是已经过 matrix 变换的四个角点。
    /// </summary>
    public void Draw(Canvas canvas, Session session, double time, Matrix3x2? projection = null,
        Func<string, double>? values = null, string? ghostId = null, Action<string, Vector2[]>? bounds = null, bool render = true)
    {
        UseFont(canvas, session);
        var tl = session.Timeline;
        double beat = tl.Bpm.Beat(time);
        foreach (var track in session.Texts.Tracks)
        {
            string text = track.At(beat);
            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }
            string suffix = track.Id == "" ? "" : "_" + track.Id;
            double M(string kind) => values?.Invoke(kind + suffix) ?? tl.Get(kind + suffix, time);
            double alpha = M("textalp"), scale = M("textscale");
            // 正在编辑的轨道即使被演出调成全透明，也要给一个可见的幽灵，否则用户看不到自己在改什么。
            // render=false 是 GAME SCENE 命中测量：透明文字仍要留下作者框，否则恰好在 fade=0 的拍上永远选不中。
            if (track.Id == ghostId && alpha <= 0) alpha = .3;
            if ((render && alpha <= 0) || Math.Abs(scale) < .001 || Math.Abs(scale) > 1000)
            {
                continue;
            }
            bool legacy = track.Id == "";
            float x = (float)(M("textX") + (legacy ? 0 : values?.Invoke("textX" + suffix + "b") ?? tl.Get("textX" + suffix + "b", time))),
                y = (float)(M("textY") + (legacy ? 0 : values?.Invoke("textY" + suffix + "b") ?? tl.Get("textY" + suffix + "b", time)));
            var rows = Wrap(text, legacy ? -1 : M("textmaxwidth") * 10);
            // sep 是原版 draw_text_ext 的基线间距；小于 0（原版写 -1）表示取字体高度。
            float step = legacy || M("textsep") < 0 ? fontHeight : (float) M("textsep"), height = fontHeight + (rows.Count - 1) * step;
            int alignH = legacy ? 1 : Mod3(M("textalignh")), alignV = legacy ? 0 : Mod3(M("textalignv"));
            float top = alignV == 1 ? -height / 2 : alignV == 2 ? -height : 0;
            uint rgb = (uint) Math.Clamp(M(legacy ? "textcolhex" : "textcolrgb"), 0, 16777215);
            // 旧工程的 textcolhex 按 GameMaker 的 BGR 书写，col_convertion 为 0 表示还没转换过，
            // 这里交换 R 与 B。删掉这个分支会让所有历史谱面的文字变色。
            if (legacy && tl.Get("col_convertion", time) == 0)
            {
                rgb = ((rgb & 255) << 16) | (rgb & 0xFF00) | ((rgb >> 16) & 255);
            }
            var color = Color.Hex(rgb).Alpha(alpha);
            var matrix = Matrix3x2.CreateScale((float) scale) * Matrix3x2.CreateRotation(-(float) M("textrot") * MathF.PI / 180) * Matrix3x2.CreateTranslation(x, y);
            matrix *= projection ?? Matrix3x2.Identity;
            float blockWidth = Math.Max(4, rows.Max(Width)), blockLeft = alignH == 1 ? -blockWidth / 2 : alignH == 2 ? -blockWidth : 0;
            bounds?.Invoke(track.Id, new[] { new Vector2(blockLeft, top), new Vector2(blockLeft + blockWidth, top),
                new Vector2(blockLeft + blockWidth, top + height), new Vector2(blockLeft, top + height) }.Select(p => Vector2.Transform(p, matrix)).ToArray());
            foreach (string row in rows)
            {
                float width = Width(row), left = alignH == 1 ? -width / 2 : alignH == 2 ? -width : 0;
                foreach (var rune in row.EnumerateRunes())
                {
                    var g = GlyphFor(rune.ToString());
                    var texture = originalAtlas ?? pages[g.page];
                    float gx = left + g.offset * fontX, gy = top - ascenderOffset;
                    if (render)
                        canvas.Polygon(texture, Vector2.Transform(new(gx, gy), matrix), Vector2.Transform(new(gx + g.w * fontX, gy), matrix),
                            Vector2.Transform(new(gx, gy + g.h * fontY), matrix), Vector2.Transform(new(gx + g.w * fontX, gy + g.h * fontY),
                            matrix), color, new(g.x / (float) texture.Width, g.y / (float) texture.Height, g.w / (float) texture.Width,
                            g.h / (float) texture.Height));
                    left += g.advance;
                }
                top += step;
            }
        }
    }

    /// <summary>对齐值先四舍五入再对 3 取模，负数同样落进 0-2：0=左/上，1=中，2=右/下。</summary>
    static int Mod3(double v) => ((int) Math.Round(v) % 3 + 3) % 3;
    public void Dispose()
    {
        originalAtlas?.Dispose();
        foreach (var p in pages)
        {
            p.Dispose();
        }
    }
}
