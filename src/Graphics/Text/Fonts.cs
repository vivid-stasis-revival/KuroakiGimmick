using System.Text;

namespace KuroakiGimmick.Graphics;

/// <summary>
/// 界面文字绘制。整套交互界面——viewer、editor、文档与信息卡片——共用一份 Noto Sans SC，
/// 中文、英文、数字与标识符不再在一行里切换字体族。
///
/// 字形在绘制时按目标的物理像素密度现场栅格化（见 <see cref="UiFont"/>），因此同一段文字
/// 在 100% DPI 的窗口、Retina 屏幕和 4K 卡片导出里都按各自的实际尺寸生成，不存在把低分辨率
/// 位图放大采样的情况。字体随程序集内嵌，新增文案不需要重新生成任何资源。
///
/// 场景歌词与游戏 HUD 不走这里：它们各自使用目标游戏的原版图集，以保持与游戏一致。
/// </summary>
public sealed partial class Fonts : IDisposable
{
    readonly UiFont ui = UiFont.Load();

    /// <summary>等宽请求的格宽基准，以及字体缺字时的替身。</summary>
    static readonly Rune Digit = new('0'), Missing = new('?');

    /// <summary>
    /// 等宽模式下每个字形占据的格宽，按 em 归一化。信息卡片的统计面板靠它右对齐数字，
    /// 数值变化时列不会左右跳动。
    /// </summary>
    float Cell(bool bold) => ui.AdvanceEm(Digit, bold);

    /// <summary>字体没有的码位一律退到 '?'，与旧图集时代的可见缺字行为一致。</summary>
    Rune Resolve(Rune rune, bool bold) => ui.Covers(rune, bold) ? rune : Missing;

    /// <summary>
    /// 等宽请求下一个字形占据的步进。格宽取数字的宽度，窄字形一律补齐到它，数字列因此严格对齐；
    /// 比格宽更宽的字形保留自身宽度——汉字整格是一个 em，硬压到数字格宽会让相邻的字叠在一起。
    /// </summary>
    float Advance(Rune rune, float size, bool monospaced, bool bold, float cell)
    {
        float natural = ui.AdvanceEm(rune, bold) * size;
        return monospaced ? MathF.Max(cell, natural) : natural;
    }

    /// <summary>
    /// 量宽必须与 Text 走同一条步进计算，否则居中、右对齐和 maxWidth 裁剪会与实际绘制错位。
    /// 步进只由 em 归一化的字体度量和逻辑字号决定，与栅格像素尺寸无关，
    /// 因此同一段文字在不同 DPI 的显示器之间移动时宽度不变。
    /// </summary>
    public float Measure(string text, float size, bool monospaced = false, bool unified = false, bool bold = false)
    {
        float cell = monospaced ? Cell(bold) * size : 0;
        float width = 0;
        foreach (var rune in text.EnumerateRunes())
        {
            width += Advance(Resolve(rune, bold), size, monospaced, bold, cell);
        }
        return width;
    }

    /// <summary>
    /// <paramref name="unified"/> 保留给既有调用方：整套界面本就共用同一字体，
    /// 不再存在需要切换到独立文档字体的情况。
    /// </summary>
    public void Text(Canvas c, string text, float x, float y, float size, Color color,
        bool monospaced = false, float maxWidth = float.MaxValue, bool unified = false, bool bold = false)
    {
        // 按当前绘制目标的像素密度决定栅格尺寸：窗口是 1x/2x，4K 卡片导出是 3x。
        int pixelSize = Math.Max(1, (int)MathF.Round(size * c.PixelScaleY));

        // 整串需要的字形先补齐，再取图集。上传必须发生在任何 Quad 之前，
        // 否则本帧新补的字会采样到尚未上传的区域。
        foreach (var rune in text.EnumerateRunes())
        {
            ui.TryGlyph(Resolve(rune, bold), pixelSize, bold, out _);
        }
        var atlas = ui.Atlas(c.Gpu);

        // 只把整串的原点吸附到物理像素网格。逐字形的 advance 保持小数，
        // 这样在不同 DPI 的显示器之间移动时文字不会抖动。
        x = c.SnapX(x);
        y = c.SnapY(y);
        float start = x;
        float cell = monospaced ? Cell(bold) * size : 0;
        // 栅格像素回到逻辑单位：字形按 pixelSize 栅格化，但要以 size 的尺度落到画面上。
        float toLogical = size / pixelSize;

        foreach (var rune in text.EnumerateRunes())
        {
            var resolved = Resolve(rune, bold);
            float advance = Advance(resolved, size, monospaced, bold, cell);
            if (x - start + advance > maxWidth)
            {
                break;
            }
            // 空格一类没有墨迹的字形只推进步进。
            if (ui.TryGlyph(resolved, pixelSize, bold, out var glyph))
            {
                float w = glyph.W * toLogical, h = glyph.H * toLogical;
                // 等宽模式把字形在它的格子里居中；宽字形的格子就是它自身，居中不产生偏移。
                float left = monospaced ? x + (advance - w) / 2 : x + glyph.OffsetX * toLogical;
                c.Quad(atlas, new(left, y + glyph.OffsetY * toLogical, w, h), color,
                    new(glyph.X / (float)UiFont.AtlasSize, glyph.Y / (float)UiFont.AtlasSize,
                        glyph.W / (float)UiFont.AtlasSize, glyph.H / (float)UiFont.AtlasSize));
            }
            x += advance;
        }
    }

    public void Dispose() => ui.Dispose();
}
