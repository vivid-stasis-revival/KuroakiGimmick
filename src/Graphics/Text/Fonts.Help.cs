using System.Text;
using System.Text.Json;
using KuroakiGimmick.Core;

namespace KuroakiGimmick.Graphics;

public sealed partial class Fonts
{
    /// <summary>
    /// 帮助文字整行都用同一套比例 CJK 字体，包括其中的 Latin 标识符。
    /// 所有图集数值都是源像素；只有 EmSize 负责换算到界面单位，不沿用旧图集的 32px 约定。
    /// OffsetY 已经含字体 ascent，所以标点、大写字母和汉字共用同一条基线，不要再额外加 ascent。
    /// </summary>
    public sealed class HelpGlyph
    {
        public float X { get; set; }
        public float Y { get; set; }
        public float Width { get; set; }
        public float Height { get; set; }
        public float Advance { get; set; }
        public float OffsetX { get; set; }
        public float OffsetY { get; set; }
    }

    /// <summary>一张帮助图集的完整描述。Family 用于校验 regular 与 bold 是否同族；EmSize 是唯一的缩放分母。</summary>
    public sealed class HelpAtlasMetrics
    {
        public string Family { get; set; } = "";
        public string Style { get; set; } = "";
        public float EmSize { get; set; }
        public float Ascent { get; set; }
        public float Descent { get; set; }
        public Dictionary<string, HelpGlyph> Glyphs { get; set; } = new(StringComparer.Ordinal);
    }

    sealed class HelpAtlas(Texture texture, HelpAtlasMetrics metrics) : IDisposable
    {
        public Texture Texture { get; } = texture;
        public HelpAtlasMetrics Metrics { get; } = metrics;
        public void Dispose() => Texture.Dispose();
    }

    HelpAtlas? helpRegular, helpBold;
    readonly HashSet<int> missingHelpGlyphs = [];
    /// <summary>regular 与 bold 成对发布，所以这一个判断同时代表标题与正文都可用，不会只有一半。</summary>
    public bool HasReadableHelpFont => helpRegular != null && helpBold != null;
    public string HelpFontName => helpRegular?.Metrics.Family ?? "FONT ASSETS MISSING";

    /// <summary>
    /// regular 与 bold 要么一起发布，要么一起放弃并回退到旧图集；不存在只装上一半的中间状态。
    /// 失败时两张已加载的纹理都在这里释放，字段保持 null，调用方通过 HasReadableHelpFont 走回退。
    /// </summary>
    void LoadHelpAtlases(GpuDevice gpu)
    {
        HelpAtlas? regular = null, bold = null;
        try
        {
            regular = LoadHelpAtlas(gpu, "editor-help-sans");
            bold = LoadHelpAtlas(gpu, "editor-help-sans-bold");
            // 成对发布：加载不完整时绝不能让标题和正文混用不同字体族。
            if (regular.Metrics.Family != bold.Metrics.Family)
                throw new InvalidDataException("Help title and body families differ.");
            helpRegular = regular;
            helpBold = bold;
            Console.Error.WriteLine($"[font] Readable help: {regular.Metrics.Family} " +
                $"{regular.Metrics.Style} / {bold.Metrics.Style}; {regular.Metrics.Glyphs.Count} glyphs; " +
                $"{regular.Metrics.EmSize:0}px em (editor-preview.9)");
        }
        catch (Exception ex)
        {
            regular?.Dispose();
            bold?.Dispose();
            helpRegular = helpBold = null;
            Console.Error.WriteLine($"[font] Readable help unavailable: {ex.Message}. " +
                "Using legacy fallback; restore all four Assets/Fonts/editor-help-sans* files.");
        }
    }

    /// <summary>
    /// 发布前先把整份 metrics 验完：EmSize/Ascent/Descent 必须有限，'?' 必须存在，
    /// 每个字形矩形必须落在图集内且 advance/bearing 非负。任一条不满足就抛出并释放纹理，
    /// 宁可回退到旧图集，也不要在运行期拿越界 UV 去采样。
    /// </summary>
    static HelpAtlas LoadHelpAtlas(GpuDevice gpu, string name)
    {
        string root = Path.Combine(Paths.Assets, "Fonts");
        var metrics = JsonSerializer.Deserialize<HelpAtlasMetrics>(
            File.ReadAllText(Path.Combine(root, name + ".json")))
            ?? throw new InvalidDataException($"{name}: empty metrics.");
        if (!float.IsFinite(metrics.EmSize) || metrics.EmSize <= 0 ||
            !float.IsFinite(metrics.Ascent) || !float.IsFinite(metrics.Descent) ||
            metrics.Glyphs is null || !metrics.Glyphs.ContainsKey("?"))
            throw new InvalidDataException($"{name}: invalid metrics or missing replacement glyph.");
        var texture = Texture.Load(gpu, Path.Combine(root, name + ".png"), linear: true, mipmaps: true);
        try
        {
            foreach (var glyph in metrics.Glyphs.Values)
            {
                if (glyph is null || !float.IsFinite(glyph.X) || !float.IsFinite(glyph.Y) ||
                    !float.IsFinite(glyph.Width) || !float.IsFinite(glyph.Height) ||
                    !float.IsFinite(glyph.Advance) || !float.IsFinite(glyph.OffsetX) || !float.IsFinite(glyph.OffsetY) ||
                    glyph.X < 0 || glyph.Y < 0 || glyph.Width < 0 || glyph.Height < 0 || glyph.Advance < 0 ||
                    glyph.X + glyph.Width > texture.Width || glyph.Y + glyph.Height > texture.Height)
                    throw new InvalidDataException($"{name}: glyph outside atlas or invalid advance/bearing.");
            }
            return new HelpAtlas(texture, metrics);
        }
        catch
        {
            texture.Dispose();
            throw;
        }
    }

    HelpAtlas? GetHelpAtlas(bool bold) => bold ? helpBold : helpRegular;

    HelpGlyph HelpGlyphFor(HelpAtlas atlas, Rune rune)
    {
        if (atlas.Metrics.Glyphs.TryGetValue(rune.ToString(), out var glyph)) return glyph;
        // 绝不把另一套字体悄悄混进中文段落。运行期的对象标识符可能含有这张有限图集里没有的字符；
        // 宁可暴露出来（每个码位只记一次日志），也不要假装覆盖，回退到同一字体族自己的 '?'。
        if (missingHelpGlyphs.Add(rune.Value))
            Console.Error.WriteLine($"[font] Help atlas missing U+{rune.Value:X4}; using same-face '?'.");
        return atlas.Metrics.Glyphs["?"];
    }

    float MeasureHelp(string text, float size, HelpAtlas atlas)
    {
        float width = 0, scale = size / atlas.Metrics.EmSize;
        foreach (var rune in text.EnumerateRunes()) width += HelpGlyphFor(atlas, rune).Advance * scale;
        return width;
    }

    /// <summary>
    /// 与 Fonts.Text 一样只吸附整串原点，advance 保持小数。目标位置直接用 OffsetX/OffsetY，
    /// 它们已经含 ascent，(x, y) 因此是行顶而不是基线；宽高为 0 的字形（空格）不提交四边形。
    /// </summary>
    void DrawHelp(Canvas canvas, string text, float x, float y, float size, Color color,
        float maxWidth, HelpAtlas atlas)
    {
        if (size <= 0 || maxWidth <= 0) return;
        float scale = size / atlas.Metrics.EmSize;
        x = canvas.SnapX(x);
        y = canvas.SnapY(y);
        float start = x;
        foreach (var rune in text.EnumerateRunes())
        {
            var glyph = HelpGlyphFor(atlas, rune);
            float advance = glyph.Advance * scale;
            if (x - start + advance > maxWidth) break;
            if (glyph.Width > 0 && glyph.Height > 0)
                canvas.Quad(atlas.Texture,
                    new(x + glyph.OffsetX * scale, y + glyph.OffsetY * scale,
                        glyph.Width * scale, glyph.Height * scale), color,
                    new(glyph.X / atlas.Texture.Width, glyph.Y / atlas.Texture.Height,
                        glyph.Width / atlas.Texture.Width, glyph.Height / atlas.Texture.Height));
            x += advance;
        }
    }
}
