using System.Globalization;
using System.Numerics;
using KuroakiGimmick.Core;

namespace KuroakiGimmick.Graphics;

/// <summary>
/// 乐曲信息卡片的绘制。卡面始终按 1280x720 的逻辑坐标绘制，实际像素由调用方给定，
/// 因此换分辨率不需要动任何一个坐标。统计面板是像素画：它按 226x92 设计，在卡面上正好 3 倍放大。
/// 卡面文字保持英文 —— 卡片是拿来分享的图片，界面语言不应该改变别人收到的那张图。
/// </summary>
public sealed class InfoCardRenderer : IDisposable
{
    public const int BaseWidth = 1280, BaseHeight = 720;
    /// <summary>统计面板的像素画布尺寸与它在卡面上的放大倍数。</summary>
    const float PanelW = 226, PanelH = 92, PanelScale = 3, PanelX = 548, PanelY = 414;

    // 与编辑器主题同源的一套暗红配色，独立于用户选的 UI 主题：同一张谱面导出的卡片应当长得一样。
    static readonly Color Bg0 = Color.Hex(0x09080B), Bg1 = Color.Hex(0x151014), Bg2 = Color.Hex(0x1C1318);
    static readonly Color Bg3 = Color.Hex(0x24171D), Stripe0 = Color.Hex(0x21151B), Stripe1 = Color.Hex(0x57303A);
    static readonly Color Stripe2 = Color.Hex(0x824153);
    static readonly Color TextColor = Color.Hex(0xFFF6F8), Subtext = Color.Hex(0xC7AFB7), ArtistColor = Color.Hex(0xD06179);
    static readonly Color Accent0 = Color.Hex(0xFF8CA0), Accent1 = Color.Hex(0xF04461), Accent2 = Color.Hex(0xD91F40);
    static readonly Color[] PatternColors = [Bg2, Stripe0, Stripe1, Bg3, Stripe2];

    Target? target;
    Target? panelPattern;
    Texture? jacket;
    string? jacketPath;

    /// <summary>条的颜色随数值从浅到深；200 以上的部分另用白色压在上面，与原版统计面板的读法一致。</summary>
    static Color BarColor(double value)
    {
        double t = Math.Clamp(value / 200.0, 0, 1);
        return t < .5 ? Mix(Accent0, Accent1, t / .5) : Mix(Accent1, Accent2, (t - .5) / .5);
    }

    static Color Mix(Color a, Color b, double t) => new(a.R + (b.R - a.R) * (float)t, a.G + (b.G - a.G) * (float)t,
        a.B + (b.B - a.B) * (float)t, a.A + (b.A - a.A) * (float)t);

    /// <summary>原版的取整规则，用在条宽换算上，避免与数字显示各用一套。</summary>
    static float Round(double v) => SongStats.Round(v);

    /// <summary>
    /// 画一张卡片并返回离屏 target。<paramref name="pixelWidth"/> 只改变像素密度，版面不变；
    /// target 由本对象持有并复用，调用方只读不销毁。只能在窗口线程调用。
    /// </summary>
    public Target Render(Canvas canvas, Fonts fonts, GameUiRenderer gameUi, Session session, SongInfoCard card, int pixelWidth)
    {
        int width = Math.Clamp(pixelWidth, BaseWidth, 7680), height = width * 9 / 16;
        if (target == null)
        {
            target = new(canvas.Gpu, width, height, linear: true);
        }
        else if (target.Texture.Width != width || target.Texture.Height != height)
        {
            canvas.Flush();
            target.Resize(width, height);
        }
        // The reference Python draws the pattern at 226x92 and scales it with NEAREST.
        // Do not rasterize its diagonals directly at card/export resolution.
        EnsurePanelPattern(canvas);
        canvas.Begin(target, width, height, BaseWidth, BaseHeight, Bg0);
        // 安静的网格底纹，避免大片纯色看起来像没画完。
        for (int x = 0; x < BaseWidth; x += 32) canvas.Fill(new(x, 0, 1, BaseHeight), Bg2);
        for (int y = 0; y < BaseHeight; y += 32) canvas.Fill(new(0, y, BaseWidth, 1), Bg2);
        DrawJacket(canvas, fonts, session, card);
        DrawHeader(canvas, fonts, card);
        DrawRows(canvas, fonts, gameUi, session, card);
        DrawStats(canvas, fonts, gameUi, session, card);
        return target;
    }

    void DrawJacket(Canvas canvas, Fonts fonts, Session session, SongInfoCard card)
    {
        // 封面 440x440，外面单独画亮框，避免把图二次重采样。
        canvas.Fill(new(48, 94, 452, 452), Bg0);
        canvas.Border(new(50, 96, 448, 448), TextColor, 4);
        if (card.JacketPath != null && File.Exists(card.JacketPath))
        {
            if (jacketPath != card.JacketPath)
            {
                canvas.Flush();
                jacket?.Dispose();
                jacket = null;
                // 封面损坏不应该让整张卡片画不出来，退回 NO JACKET 占位。
                try { jacket = Texture.Load(canvas.Gpu, card.JacketPath); }
                catch (Exception ex) when (ex is IOException or InvalidDataException) { jacket = null; }
                jacketPath = card.JacketPath;
            }
        }
        else if (jacket != null)
        {
            canvas.Flush();
            jacket.Dispose();
            jacket = null;
            jacketPath = null;
        }
        canvas.Fill(new(54, 100, 440, 440), Bg1);
        if (jacket == null)
        {
            fonts.Text(canvas, "NO JACKET", 274 - fonts.Measure("NO JACKET", 30) / 2, 305, 30, Subtext, bold: true);
            return;
        }
        // 等比放大后居中；低分辨率封面保持硬边，与游戏里的观感一致。
        int frames = session.Jackets.Animated && card.JacketPath == session.Jackets.DefaultPath
            ? Math.Max(1, session.Jackets.AnimatedFrames) : 1;
        float sourceWidth = jacket.Width / (float)frames;
        float factor = Math.Min(440f / sourceWidth, 440f / jacket.Height);
        float w = Math.Max(1, Round(sourceWidth * factor)), h = Math.Max(1, Round(jacket.Height * factor));
        // Information cards use frame 0 so an animated horizontal strip is not displayed as one stretched ultra-wide image.
        canvas.Quad(jacket, new(54 + (440 - w) / 2, 100 + (440 - h) / 2, w, h), Color.White, new(0, 0, 1f / frames, 1));
    }

    void DrawHeader(Canvas canvas, Fonts fonts, SongInfoCard card)
    {
        // 标题按可用宽度逐级缩小，宁可小也不要截断成半个字。
        float size = 46;
        while (size > 20 && fonts.Measure(card.Title, size, bold: true) > 680) size -= 2;
        fonts.Text(canvas, card.Title, 550, 76, size, TextColor, maxWidth: 690, bold: true);
        float artistSize = 27;
        while (artistSize > 14 && fonts.Measure(card.Artist, artistSize) > 680) artistSize -= 1;
        fonts.Text(canvas, card.Artist, 552, 140, artistSize, ArtistColor, maxWidth: 690);
    }

    void DrawRows(Canvas canvas, Fonts fonts, GameUiRenderer gameUi, Session session, SongInfoCard card)
    {
        (string Label, string Value)[] rows =
        [
            ("DIFFICULTY", card.DisplayDifficulty),
            ("BPM", card.BpmDisplay),
            ("LENGTH", card.LengthDisplay),
            ("NOTES", card.NoteCount.ToString("N0", CultureInfo.InvariantCulture)),
            ("DESIGNER", card.Designer ?? "-")
        ];
        float y = 196;
        foreach (var (label, value) in rows)
        {
            fonts.Text(canvas, label, 552, y, 21, Subtext, monospaced: true);
            fonts.Text(canvas, value, 760, y, 21, TextColor, maxWidth: 300);
            if (label == "DIFFICULTY")
            {
                // 难度徽章用游戏原版精灵，4 倍放大后是 176x44；资源包缺失时退回纯文字等级。
                if (!gameUi.DrawDifficultyBadge(session, card.Level ?? "", 1060, y - 10, 4) && card.Level is { Length: > 0 })
                {
                    fonts.Text(canvas, card.Level, 1060, y, 21, TextColor, monospaced: true);
                }
            }
            y += 42;
        }
    }

    /// <summary>
    /// Build the stat backdrop once on its native 226x92 pixel grid, then sample it
    /// with NEAREST on the card. Drawing these polygons directly at 3x/4K resolution
    /// produces smooth diagonal stripes instead of the reference's pixel stair-steps.
    /// </summary>
    void EnsurePanelPattern(Canvas canvas)
    {
        if (panelPattern != null) return;
        // Target defaults to a nearest-neighbour sampler. Its physical and logical
        // dimensions must match so each polygon is rasterized to one source pixel.
        panelPattern = new Target(canvas.Gpu, (int)PanelW, (int)PanelH);
        canvas.Begin(panelPattern, (int)PanelW, (int)PanelH, PanelW, PanelH, Bg1);
        // Render-target bounds provide the same clipping as PIL's 226x92 canvas.
        for (int k = -(int)PanelH; k < PanelW + 24; k += 20)
        {
            // Python's k // 20 floors negative values; C# integer division truncates.
            int stripe = (int)Math.Floor(k / 20.0);
            var c = PatternColors[(stripe % PatternColors.Length + PatternColors.Length) % PatternColors.Length];
            // Canvas.Polygon expects TL, TR, BL, BR, not polygon-outline order.
            canvas.Polygon(canvas.White,
                new Vector2(k, 0), new Vector2(k + 10, 0),
                new Vector2(k + 26, PanelH), new Vector2(k + 36, PanelH), c);
        }
        for (int y = 2; y < PanelH - 2; y += 8)
        {
            for (int x = 4 + (y / 8) % 3 * 7; x < PanelW - 3; x += 27)
            {
                var c = PatternColors[(x + y) % PatternColors.Length];
                canvas.Fill(new Rect(x, y, Math.Min(PanelW - 1, x + 5) - x,
                    Math.Min(PanelH - 1, y + 3) - y), c);
            }
        }
        // Queue the finished low-res pass before the full card samples this target.
        canvas.Flush();
    }

    void DrawPanelPattern(Canvas canvas)
    {
        canvas.Quad(panelPattern!.Texture, Panel(0, 0, PanelW, PanelH), Color.White);
    }

    static Rect Panel(float x, float y, float w, float h) =>
        new(PanelX + x * PanelScale, PanelY + y * PanelScale, w * PanelScale, h * PanelScale);

    static Vector2 Point(float x, float y) => new(PanelX + x * PanelScale, PanelY + y * PanelScale);

    void DrawStats(Canvas canvas, Fonts fonts, GameUiRenderer gameUi, Session session, SongInfoCard card)
    {
        var s = card.Stats;
        (string Name, double Value)[] all =
        [
            ("CHIP", s.Chip), ("TECH", s.Tech), ("STREAM", s.Stream), ("CHORD", s.Chord), ("BURST", s.Burst),
            ("GIMMICK", s.Gimmick)
        ];
        bool gimmick = card.HasGimmick;
        var shown = gimmick ? all : all[..5];
        DrawPanelPattern(canvas);
        // 两道边框：外白内黑，与原版一致。
        canvas.Border(Panel(0, 0, PanelW, PanelH), TextColor, PanelScale);
        canvas.Border(Panel(1, 1, PanelW - 2, PanelH - 2), Bg0, PanelScale);
        // 六项时行距 14、五项时 16，与标签精灵的两种排布对应。
        float[] rowY = gimmick ? [5, 19, 33, 47, 61, 75] : [5, 21, 37, 53, 69];
        const float barX = 86, barMax = 78, barH = 10, valueGap = 4, valueRight = 214;
        // 数值只能使用满条右侧的独立列，并与条保留四个面板像素。高位数值缩小，不反向侵占标签或条。
        float valueMaxWidth = (valueRight - (barX + barMax + valueGap)) * PanelScale;
        bool labels = gameUi.DrawStatLabels(session, Panel(8, 6, 0, 0).X, Panel(8, 6, 0, 0).Y, 2 * PanelScale, gimmick);
        for (int i = 0; i < shown.Length; i++)
        {
            var (name, value) = shown[i];
            float y = rowY[i];
            if (!labels)
            {
                // 没有标签精灵时退回等宽字体，宁可样子不同也要能读出是哪一项。
                fonts.Text(canvas, name, Panel(10, y + 1, 0, 0).X, Panel(10, y + 1, 0, 0).Y, 9 * PanelScale, TextColor, monospaced: true);
            }
            int rounded = SongStats.Round(value);
            if (!gameUi.DrawStatNumber(session, Panel(valueRight, y + 1, 0, 0).X, Panel(valueRight, y + 1, 0, 0).Y,
                PanelScale * 2, rounded, valueMaxWidth))
            {
                string number = rounded.ToString(CultureInfo.InvariantCulture);
                float size = 9 * PanelScale;
                float fitted = GameUiRenderer.FitScale(fonts.Measure(number, size, monospaced: true), size, valueMaxWidth);
                fonts.Text(canvas, number, Panel(valueRight, y, 0, 0).X - fonts.Measure(number, fitted, monospaced: true),
                    Panel(valueRight, y, 0, 0).Y + (size - fitted) / 2, fitted, TextColor, monospaced: true);
            }
            if (value <= 0)
            {
                continue;
            }
            if (value > 200)
            {
                // 200 封顶后，超出的部分改用白条从左侧叠在满条上，与原版统计面板的读法一致。
                canvas.Fill(Panel(barX, y, barMax, barH), BarColor(200));
                float extra = Round(barMax * (Math.Min(Math.Max(value - 200, 0), 200) / 200.0));
                if (extra > 0) canvas.Fill(Panel(barX, y, extra, barH), TextColor);
            }
            else
            {
                float width = Round(barMax * (value / 200.0));
                if (width > 0) canvas.Fill(Panel(barX, y, width, barH), BarColor(value));
            }
        }
    }

    public void Dispose()
    {
        jacket?.Dispose();
        target?.Dispose();
        panelPattern?.Dispose();
        jacket = null;
        target = null;
        panelPattern = null;
    }
}
