using System.Runtime.InteropServices;
using System.Text;
using StbTrueTypeSharp;

namespace KuroakiGimmick.Graphics;

/// <summary>
/// 界面文字的运行时字形栅格化与动态图集。
///
/// 字形按绘制目标的物理像素密度现场栅格化，因此 4K 卡片导出和高 DPI 屏幕拿到的是
/// 按实际尺寸生成的字形，而不是为低分辨率烘焙、再被放大采样的位图。图集只是内部缓存：
/// 用到哪个字就补哪个字，没有任何需要随界面文案重新生成的资源，加字不需要重新烘焙。
///
/// 这里只服务交互界面。场景歌词与游戏 HUD 走各自的原版图集，保持与目标游戏一致。
/// </summary>
public sealed class UiFont : IDisposable
{
    /// <summary>图集边长。够放下常用字号下的整屏中文，装不下时整体作废重建，而不是无限增长。</summary>
    public const int AtlasSize = 2048;

    /// <summary>相邻字形之间留一像素空白，避免线性采样把邻居的边缘带进来。</summary>
    const int Padding = 1;

    /// <summary>图集内的源矩形与相对文本行顶的偏移，单位都是栅格像素；步进另以 em 归一化保存。</summary>
    public sealed class Glyph
    {
        public int X, Y, W, H;
        public float OffsetX, OffsetY;
    }

    /// <summary>一个字重。stbtt 直接读字体文件内存，因此数组必须固定住，不能让 GC 搬走。</summary>
    sealed class Face : IDisposable
    {
        public readonly StbTrueType.stbtt_fontinfo Info = new();
        /// <summary>字体单位到 em 的比例；乘以逻辑字号即得逻辑单位。</summary>
        public readonly float EmScale;
        public readonly float AscentEm;
        GCHandle pin;

        public unsafe Face(byte[] data)
        {
            pin = GCHandle.Alloc(data, GCHandleType.Pinned);
            var head = (byte*)pin.AddrOfPinnedObject();
            if (StbTrueType.stbtt_InitFont(Info, head, 0) == 0)
            {
                pin.Free();
                throw new InvalidDataException("Unreadable UI font: stb_truetype could not parse the outlines.");
            }
            EmScale = StbTrueType.stbtt_ScaleForMappingEmToPixels(Info, 1);
            // 排版用 OS/2 的 typo 升部，而不是 hhea 的行高升部。hhea 为了容纳重音和 CJK
            // 的完整行盒，在 Noto Sans SC 上是 1.16em，拿它当"行顶到基线"会把整套界面压低；
            // typo 升部 0.88em 与界面既有的 0.89em 版式约定一致，全部布局不必重新标定。
            int ascent, descent, lineGap;
            StbTrueType.stbtt_GetFontVMetricsOS2(Info, &ascent, &descent, &lineGap);
            if (ascent == 0)
            {
                StbTrueType.stbtt_GetFontVMetrics(Info, &ascent, &descent, &lineGap);
            }
            AscentEm = ascent * EmScale;
        }

        public void Dispose()
        {
            if (pin.IsAllocated)
            {
                pin.Free();
            }
        }
    }

    readonly Face regular, semiBold;
    readonly Dictionary<long, Glyph?> cache = [];
    readonly byte[] pixels = new byte[AtlasSize * AtlasSize * 4];
    /// <summary>货架式打包：一行装满就抬到下一行，不做旋转或回填。</summary>
    int penX, penY, shelfHeight;
    bool dirty, exhausted;
    Texture? atlas;

    /// <summary>行顶到基线的距离，按 em 归一化；乘以字号即得逻辑单位。</summary>
    public float AscentEm => regular.AscentEm;

    /// <summary>栅格化与打包全在 CPU 上完成，构造时不需要 GPU；纹理推迟到第一次 <see cref="Atlas"/> 才建。</summary>
    public UiFont(byte[] regularFont, byte[] semiBoldFont)
    {
        regular = new(regularFont);
        try
        {
            semiBold = new(semiBoldFont);
        }
        catch
        {
            regular.Dispose();
            throw;
        }
        ResetPixels();
    }

    /// <summary>图集整张是透明的白：RGB 保持白色，覆盖率只存在 alpha 里，边缘不会向黑色收敛。</summary>
    void ResetPixels()
    {
        Array.Clear(pixels);
        for (int i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = pixels[i + 1] = pixels[i + 2] = 255;
        }
    }

    /// <summary>从内嵌资源装载两个字重。字体随程序集发布，不依赖本地 Assets 目录或系统字体。</summary>
    public static UiFont Load()
    {
        return new(Read("NotoSansSC-Regular.ttf"), Read("NotoSansSC-SemiBold.ttf"));

        static byte[] Read(string name)
        {
            var assembly = System.Reflection.Assembly.GetExecutingAssembly();
            using var stream = assembly.GetManifestResourceStream("KuroakiGimmick.Fonts." + name)
                ?? throw new InvalidDataException("Missing embedded UI font: " + name);
            var bytes = new byte[stream.Length];
            stream.ReadExactly(bytes);
            return bytes;
        }
    }

    /// <summary>字形缓存的键：码位、栅格像素尺寸与字重三者共同决定一张位图。</summary>
    static long Key(int codepoint, int pixelSize, bool bold) =>
        (uint)codepoint | ((long)pixelSize << 24) | (bold ? 1L << 40 : 0);

    /// <summary>某个码位在该字重下的步进，按 em 归一化；字体没有这个字时返回 0。</summary>
    public unsafe float AdvanceEm(Rune rune, bool bold)
    {
        var face = bold ? semiBold : regular;
        int index = StbTrueType.stbtt_FindGlyphIndex(face.Info, rune.Value);
        if (index == 0)
        {
            return 0;
        }
        int advance, bearing;
        StbTrueType.stbtt_GetGlyphHMetrics(face.Info, index, &advance, &bearing);
        return advance * face.EmScale;
    }

    public bool Covers(Rune rune, bool bold) =>
        StbTrueType.stbtt_FindGlyphIndex((bold ? semiBold : regular).Info, rune.Value) != 0;

    /// <summary>
    /// 取一个已栅格化的字形，必要时现场栅格化并补进图集。空白字形（空格）返回 false 但不算缺字，
    /// 调用方应当只推进步进。图集当帧装满时同样返回 false：宁可这一帧少画一个字，
    /// 也不能中途重排图集，那会让本帧已经提交的字形指向错误的位置。
    /// </summary>
    public unsafe bool TryGlyph(Rune rune, int pixelSize, bool bold, out Glyph glyph)
    {
        glyph = null!;
        if (pixelSize <= 0)
        {
            return false;
        }
        long key = Key(rune.Value, pixelSize, bold);
        if (cache.TryGetValue(key, out var cached))
        {
            glyph = cached!;
            return cached != null;
        }
        var face = bold ? semiBold : regular;
        int index = StbTrueType.stbtt_FindGlyphIndex(face.Info, rune.Value);
        if (index == 0)
        {
            cache[key] = null;
            return false;
        }
        float scale = StbTrueType.stbtt_ScaleForMappingEmToPixels(face.Info, pixelSize);
        int x0, y0, x1, y1;
        StbTrueType.stbtt_GetGlyphBitmapBox(face.Info, index, scale, scale, &x0, &y0, &x1, &y1);
        int w = x1 - x0, h = y1 - y0;
        if (w <= 0 || h <= 0)
        {
            // 空格一类没有墨迹的字形：记成"有这个字但没有位图"，靠步进推进即可。
            cache[key] = null;
            return false;
        }
        if (!Place(w, h, out int ax, out int ay))
        {
            exhausted = true;
            return false;
        }
        // stbtt 输出 8 位覆盖率。逐行写进图集的 alpha 通道，RGB 已经是白色。
        var coverage = new byte[w * h];
        fixed (byte* target = coverage)
        {
            StbTrueType.stbtt_MakeGlyphBitmap(face.Info, target, w, h, w, scale, scale, index);
        }
        for (int row = 0; row < h; row++)
        {
            int destination = ((ay + row) * AtlasSize + ax) * 4 + 3;
            int source = row * w;
            for (int column = 0; column < w; column++)
            {
                pixels[destination + column * 4] = coverage[source + column];
            }
        }
        glyph = new()
        {
            X = ax,
            Y = ay,
            W = w,
            H = h,
            OffsetX = x0,
            // stbtt 的字形盒以基线为原点、向下为正；界面按文本行顶定位，因此补上升部。
            OffsetY = face.AscentEm * pixelSize + y0
        };
        cache[key] = glyph;
        dirty = true;
        return true;
    }

    bool Place(int w, int h, out int x, out int y)
    {
        x = y = 0;
        if (w + Padding > AtlasSize || h + Padding > AtlasSize)
        {
            return false;
        }
        if (penX + w + Padding > AtlasSize)
        {
            penX = 0;
            penY += shelfHeight + Padding;
            shelfHeight = 0;
        }
        if (penY + h + Padding > AtlasSize)
        {
            return false;
        }
        x = penX;
        y = penY;
        penX += w + Padding;
        shelfHeight = Math.Max(shelfHeight, h);
        return true;
    }

    /// <summary>
    /// 把本帧新补的字形传上 GPU，并返回图集纹理。必须在窗口线程、绘制这些字形之前调用。
    /// 上一帧装满过就在这里整体作废：此时上一帧的几何已经提交完毕，重排不会影响任何在途绘制。
    /// </summary>
    public Texture Atlas(GpuDevice gpu)
    {
        if (exhausted)
        {
            cache.Clear();
            ResetPixels();
            penX = penY = shelfHeight = 0;
            exhausted = false;
            dirty = true;
        }
        // 字形按目标像素密度栅格化，不存在缩小采样，因此不需要 mip 链；
        // 线性过滤只用来吸收吸附后残留的亚像素偏移。
        atlas ??= new(gpu, AtlasSize, AtlasSize, pixels, linear: true);
        if (dirty)
        {
            gpu.Upload(atlas, pixels);
            dirty = false;
        }
        return atlas;
    }

    public void Dispose()
    {
        atlas?.Dispose();
        regular.Dispose();
        semiBold.Dispose();
    }
}
