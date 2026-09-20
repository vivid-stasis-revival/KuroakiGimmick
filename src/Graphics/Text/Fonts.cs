using System.Text;
using System.Text.Json;
using KuroakiGimmick.Core;

namespace KuroakiGimmick.Graphics;

/// <summary>
/// 界面字体图集的加载与文字批处理。
///
/// 中文主界面使用内嵌 IBM Plex Sans SC Medium；英文保持原有 sans / mono / cjk-editor。
/// 中文数字按统一步进绘制，计时不会抖动。Editor 的 gimmick 帮助卡可显式要求
/// unified 模式，让中文、英文与数字全部使用独立的 Noto Sans CJK SC 比例字体。
/// 帮助字体拥有自己的 em 尺寸、字形轴承与字重，不复用旧图集的 32px 缩放约定。
/// </summary>
public sealed partial class Fonts : IDisposable
{
    /// <summary>图集内的源像素矩形与步进；数值按 32px 设计尺寸标定，绘制时统一乘以 size/32。</summary>
    public sealed class Glyph
    {
        public float x { get; set; }
        public float y { get; set; }
        public float w { get; set; }
        public float h { get; set; }
        public float advance { get; set; }
    }

    readonly Texture sans, mono;
    readonly Dictionary<string, Glyph> sansGlyphs, monoGlyphs;

    Texture? cjk;
    Dictionary<string, Glyph>? cjkGlyphs;

    /// <summary>
    /// 所有图集一律带 mipmap 上传。界面文字在 100% DPI 下常被缩到 9-12px，属于严重缩小，
    /// 必须依赖 Texture 里按 alpha 加权的预乘 mip 链，否则边缘会发灰、细笔画会消失。
    /// </summary>
    public Fonts(GpuDevice gpu)
    {
        sans = Texture.Load(gpu, Path.Combine(Paths.Assets, "Fonts", "sans.png"), linear: true, mipmaps: true);
        mono = Texture.Load(gpu, Path.Combine(Paths.Assets, "Fonts", "mono.png"), linear: true, mipmaps: true);
        sansGlyphs = JsonSerializer.Deserialize<Dictionary<string, Glyph>>(
            File.ReadAllText(Path.Combine(Paths.Assets, "Fonts", "sans.json")))!;
        monoGlyphs = JsonSerializer.Deserialize<Dictionary<string, Glyph>>(
            File.ReadAllText(Path.Combine(Paths.Assets, "Fonts", "mono.json")))!;

        LoadCjkAtlas(gpu);
        LoadHelpAtlases(gpu);
        LoadUiAtlases(gpu);
    }

    /// <summary>CJK 回退图集缺失或损坏不算致命错误：只把原因写进 stderr，ASCII 界面继续可用。</summary>
    void LoadCjkAtlas(GpuDevice gpu)
    {
        string imagePath = Path.Combine(Paths.Assets, "Fonts", "cjk-editor.png");
        string metricsPath = Path.Combine(Paths.Assets, "Fonts", "cjk-editor.json");

        try
        {
            if (!File.Exists(imagePath) || !File.Exists(metricsPath))
            {
                Console.Error.WriteLine($"[font] Editor unified atlas missing: {imagePath}");
                return;
            }

            var metrics = JsonSerializer.Deserialize<Dictionary<string, Glyph>>(File.ReadAllText(metricsPath));
            if (metrics is null || metrics.Count == 0)
                throw new InvalidDataException("Editor unified glyph table is empty.");

            // 字形表校验通过之后才创建纹理。纹理创建失败时现有的 ASCII 界面仍然可用，
            // 错误在 stderr 可见，不做静默吞掉。
            var texture = Texture.Load(gpu, imagePath, linear: true, mipmaps: true);
            cjkGlyphs = metrics;
            cjk = texture;
            Console.Error.WriteLine($"[font] Editor unified atlas loaded: {metrics.Count} glyphs");
        }
        catch (Exception ex)
        {
            cjk?.Dispose();
            cjk = null;
            cjkGlyphs = null;
            Console.Error.WriteLine($"[font] Editor unified atlas unavailable: {ex.Message}");
        }
    }

    bool TryCjk(Rune rune, out Texture texture, out Glyph glyph)
    {
        texture = null!;
        glyph = null!;

        var tex = cjk;
        var table = cjkGlyphs;
        if (tex is null || table is null)
            return false;

        string key = rune.ToString();
        if (!table.TryGetValue(key, out var candidate) || candidate is null)
            return false;

        texture = tex;
        glyph = candidate;
        return true;
    }

    /// <summary>量宽必须与 Text 走同一条回退链，否则居中、右对齐和 maxWidth 裁剪会与实际绘制错位。</summary>
    public float Measure(string text, float size, bool monospaced = false, bool unified = false, bool bold = false)
    {
        if (unified && GetHelpAtlas(bold) is { } helpAtlas)
            return MeasureHelp(text, size, helpAtlas);

        var glyphs = monospaced ? monoGlyphs : sansGlyphs;
        float width = 0;
        float scale = size / 32f;

        foreach (var rune in text.EnumerateRunes())
        {
            string key = rune.ToString();

            // 字体随界面语言选择，量宽与绘制走同一分支。
            if (TryUiGlyph(rune, out var uiAtlas, out var uiGlyph))
            {
                width += UiAdvance(uiAtlas, uiGlyph, rune, size);
            }
            else if (unified && TryCjk(rune, out _, out var unifiedGlyph))
            {
                width += unifiedGlyph.advance * scale;
            }
            else if (glyphs.TryGetValue(key, out var glyph))
            {
                width += glyph.advance * scale;
            }
            else if (TryCjk(rune, out _, out var cjkGlyph))
            {
                width += cjkGlyph.advance * scale;
            }
            else
            {
                width += glyphs["?"].advance * scale;
            }
        }

        return width;
    }

    /// <summary>
    /// unified 使用文档图集；中文 UI 使用 IBM Plex Sans SC Medium，英文使用原有 sans/mono/CJK。
    /// 字形四边形整体偏移 (-2,-3)*scale，这是图集烘焙时的留白补偿，改掉会让全部界面文字位移。
    /// </summary>
    public void Text(Canvas c, string text, float x, float y, float size, Color color,
        bool monospaced = false, float maxWidth = float.MaxValue, bool unified = false, bool bold = false)
    {
        if (unified && GetHelpAtlas(bold) is { } helpAtlas)
        {
            DrawHelp(c, text, x, y, size, color, maxWidth, helpAtlas);
            return;
        }

        var tex = monospaced ? mono : sans;
        var glyphs = monospaced ? monoGlyphs : sansGlyphs;

        // 只把整串的原点吸附到物理像素网格。逐字形的 advance 保持小数，
        // 这样在不同 DPI 的显示器之间移动时文字不会抖动。
        x = c.SnapX(x);
        y = c.SnapY(y);
        float start = x;
        float scale = size / 32f;

        foreach (var rune in text.EnumerateRunes())
        {
            float advance;
            string key = rune.ToString();

            if (TryUiGlyph(rune, out var uiAtlas, out var uiGlyph))
            {
                advance = UiAdvance(uiAtlas, uiGlyph, rune, size);
                if (x - start + advance > maxWidth) break;
                DrawUiGlyph(c, uiAtlas, uiGlyph, x, y, size, color);
            }
            else if (unified && TryCjk(rune, out var unifiedTexture, out var unifiedGlyph))
            {
                advance = unifiedGlyph.advance * scale;
                if (x - start + advance > maxWidth) break;

                c.Quad(unifiedTexture,
                    new(x - 2 * scale, y - 3 * scale, unifiedGlyph.w * scale, unifiedGlyph.h * scale),
                    color,
                    new(unifiedGlyph.x / unifiedTexture.Width, unifiedGlyph.y / unifiedTexture.Height,
                        unifiedGlyph.w / unifiedTexture.Width, unifiedGlyph.h / unifiedTexture.Height));
            }
            else if (glyphs.TryGetValue(key, out var glyph))
            {
                advance = glyph.advance * scale;
                if (x - start + advance > maxWidth) break;

                c.Quad(tex,
                    new(x - 2 * scale, y - 3 * scale, glyph.w * scale, glyph.h * scale),
                    color,
                    new(glyph.x / tex.Width, glyph.y / tex.Height,
                        glyph.w / tex.Width, glyph.h / tex.Height));
            }
            else if (TryCjk(rune, out var cjkTexture, out var cjkGlyph))
            {
                advance = cjkGlyph.advance * scale;
                if (x - start + advance > maxWidth) break;

                c.Quad(cjkTexture,
                    new(x - 2 * scale, y - 3 * scale, cjkGlyph.w * scale, cjkGlyph.h * scale),
                    color,
                    new(cjkGlyph.x / cjkTexture.Width, cjkGlyph.y / cjkTexture.Height,
                        cjkGlyph.w / cjkTexture.Width, cjkGlyph.h / cjkTexture.Height));
            }
            else
            {
                var missingGlyph = glyphs["?"];
                advance = missingGlyph.advance * scale;
                if (x - start + advance > maxWidth) break;

                c.Quad(tex,
                    new(x - 2 * scale, y - 3 * scale, missingGlyph.w * scale, missingGlyph.h * scale),
                    color,
                    new(missingGlyph.x / tex.Width, missingGlyph.y / tex.Height,
                        missingGlyph.w / tex.Width, missingGlyph.h / tex.Height));
            }

            x += advance;
        }
    }

    public void Dispose()
    {
        sans.Dispose();
        mono.Dispose();
        cjk?.Dispose();
        helpRegular?.Dispose();
        helpBold?.Dispose();
        uiRegular?.Dispose();
        uiBold?.Dispose();
    }
}
