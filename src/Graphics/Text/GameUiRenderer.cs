using KuroakiGimmick.Core;

namespace KuroakiGimmick.Graphics;

/// <summary>
/// 游戏 HUD 的分层绘制。PAUSE / Escape / score 可进入 application proxy；固定 HUD 与 GUI HUD 单独合成。
/// 文字和控件优先使用原版资源，图像内的文字不再绘制一次。
/// </summary>
// 对应 cc Draw_0 的共用游戏 HUD。美术与度量全部来自用户自己导出的、未经修改的游戏资源包，
// 绝不从截图里重新描一份。
public sealed partial class GameUiRenderer : IDisposable
{
    readonly Canvas canvas;
    GameUiAssets? current;
    readonly Dictionary<string, Texture> textures = [];
    readonly Dictionary<string, Dictionary<int, GameUiAssets.Glyph>> glyphs = [];
    /// <summary>统计数字精灵的不透明包围盒缓存，按文件名索引；整张透明或读不出时存 null。</summary>
    readonly Dictionary<string, Rect?> opaqueBounds = [];
    public GameUiRenderer(Canvas c) => canvas = c;
    /// <summary>
    /// 资源包身份（引用相等）变化时才重建缓存：先 Flush 把仍引用旧纹理的批次交出去，再 Dispose，
    /// 否则会释放掉本帧还在用的纹理。身份不变时必须直接返回，不能每帧重建图集。
    /// </summary>
    void Use(GameUiAssets assets)
    {
        if (ReferenceEquals(current, assets))
        {
            return;
        }
        canvas.Flush();
        Dispose();
        current = assets;
        if (assets.Data != null)
        {
            foreach (var (name, font) in assets.Data.Fonts)
            {
                glyphs[name] = font.Glyphs.GroupBy(g => g.Character).ToDictionary(g => g.Key, g => g.First());
            }
        }
    }

    /// <summary>按文件名惰性缓存；缓存的生命周期绑定当前资源包，换包时由 Use 整体丢弃。</summary>
    Texture Image(string file)
    {
        if (!textures.TryGetValue(file, out var texture))
        {
            textures[file] = texture = Texture.Load(canvas.Gpu, current!.File(file));
        }
        return texture;
    }

    /// <summary>
    /// 帧号对帧数取模环绕，负数也能落回合法区间；精灵在资源包里不存在时静默跳过，
    /// 因为旧版本导出可能缺少新增的 HUD 精灵。(x, y) 是原点位置，绘制时减去精灵自身的 Origin。
    /// scale 照 GameMaker 的 image_xscale/image_yscale 语义以原点为中心缩放。
    /// </summary>
    void Sprite(string name, int frame, float x, float y, Color colour, float scale = 1)
    {
        if (!current!.Data!.Sprites.TryGetValue(name, out var s))
        {
            return;
        }
        var texture = Image(s.Frames[(frame % s.Frames.Count + s.Frames.Count) % s.Frames.Count]);
        canvas.Quad(texture, new(x - s.OriginX * scale, y - s.OriginY * scale, s.Width * scale, s.Height * scale), colour);
    }

    /// <summary>字形回退顺序：本体码位 → U+FFFD(65533) → '?'(63)；三者都缺时返回 null，由调用方跳过该字符。</summary>
    GameUiAssets.Glyph? Glyph(string font,
        int code) => glyphs[font].GetValueOrDefault(code) ?? glyphs[font].GetValueOrDefault(65533) ?? glyphs[font].GetValueOrDefault(63);
    /// <summary>kerning 按"前一个字符"查表，所以必须顺序遍历；整串宽度最后才乘 ScaleX，与 Text 的推进方式保持一致。</summary>
    float Width(string font, string text)
    {
        var f = current!.Data!.Fonts[font];
        float width = 0;
        int previous = -1;
        foreach (var rune in text.EnumerateRunes())
        {
            if (Glyph(font, rune.Value) is { } g)
            {
                width += g.Advance + (g.Kerning.FirstOrDefault(k => k.Character == previous)?.Shift ?? 0);
                previous = rune.Value;
            }
        }
        return width * f.ScaleX;
    }

    /// <summary>居中分支复用 Width，两处的 kerning 规则必须一致；top/bottom 是原版字面的上下渐变色，不是描边。</summary>
    void Text(string font, string text, float x, float y, Color top, Color bottom, bool center = false)
    {
        var f = current!.Data!.Fonts[font];
        var texture = Image(f.File);
        int previous = -1;
        if (center)
        {
            x -= Width(font, text) / 2;
        }
        foreach (var rune in text.EnumerateRunes())
        {
            var g = Glyph(font, rune.Value);
            if (g == null)
            {
                continue;
            }
            x += (g.Kerning.FirstOrDefault(k => k.Character == previous)?.Shift ?? 0) * f.ScaleX;
            // AscenderOffset 属于图集源 V 的行偏移，只能加在 source 上。
            // 之前改成移动目标位置，结果把 Monaco CJK 字形的最后六行裁掉了。
            canvas.QuadGradient(texture, new(x + g.Offset * f.ScaleX, y, g.W * f.ScaleX, g.H * f.ScaleY), top, bottom,
                new(g.X / (float) texture.Width, (g.Y + f.AscenderOffset) / texture.Height, g.W / (float) texture.Width,
                g.H / (float) texture.Height));
            x += g.Advance * f.ScaleX;
            previous = rune.Value;
        }
    }

    /// <summary>原版游玩区与底栏的分界。Proxy 只采样上方游玩区；底栏始终固定在屏幕上。</summary>
    const float GameplayFooterY = 165;
    /// <summary>
    /// 在任意位置按任意倍率画一枚难度徽章，供信息卡片复用。相对位置与帧序同 HUD：
    /// 指示框在原点，LEVEL 字样偏移 (3,3)、数字偏移 (26,2)，各自在 +1 像素处先压一层三成黑作为阴影。
    /// 资源包缺失或等级没有对应帧时返回 false 且一笔不画，由调用方决定退回文字，不去猜一个不存在的帧。
    /// </summary>
    public bool DrawDifficultyBadge(Session session, string level, float x, float y, float scale)
    {
        Use(session.GameUi);
        if (current?.Data == null || !current.Data.Sprites.ContainsKey("sp_newdifficultyindicator"))
        {
            return false;
        }
        // 帧号先定下来再落笔。自造等级（有谱包把 SHATTER 的难度写成 17+++）没有对应帧，
        // 底板已经画下去才发现数字画不出来的话，调用方退回来的文字会压在 LEVEL 字样上糊成一团。
        int number = DifficultyNumberFrame(level);
        if (!current.Data.Sprites.TryGetValue("sp_newdifficultynumbers", out var numbers)
            || number < 0 || number >= numbers.Frames.Count)
        {
            return false;
        }
        int frame = session.Song.DifficultyFrame;
        var white = Color.White;
        var shadow = Color.Hex(0).Alpha(.3);
        Sprite("sp_newdifficultyindicator", frame, x, y, white, scale);
        Sprite("sp_newdifficultylevel", frame, x + 4 * scale, y + 4 * scale, shadow, scale);
        Sprite("sp_newdifficultylevel", frame, x + 3 * scale, y + 3 * scale, white, scale);
        Sprite("sp_newdifficultynumbers", number, x + 27 * scale, y + 3 * scale, shadow, scale);
        Sprite("sp_newdifficultynumbers", number, x + 26 * scale, y + 2 * scale, white, scale);
        return true;
    }

    /// <summary>
    /// 等级文字到数字精灵帧号。0-18 是普通等级，"9+" 到 "17+" 接在其后，因此带加号的映射为 plus+10；
    /// 认不出的写法返回 -1。
    /// </summary>
    public static int DifficultyNumberFrame(string? level)
    {
        if (string.IsNullOrEmpty(level))
        {
            return -1;
        }
        if (level.EndsWith('+') && int.TryParse(level[..^1], out int plus) && plus is >= 9 and <= 17)
        {
            return plus + 10;
        }
        return int.TryParse(level, out int number) && number is >= 0 and <= 18 ? number : -1;
    }

    /// <summary>
    /// 统计面板的标签精灵。第 0 帧是含 GIMMICK 的六项排布，第 1 帧是五项；
    /// <paramref name="scale"/> 是一个源像素对应的目标像素数。资源缺失时返回 false，由调用方退回文字。
    /// </summary>
    public bool DrawStatLabels(Session session, float x, float y, float scale, bool gimmick)
    {
        Use(session.GameUi);
        if (current?.Data == null || !current.Data.Sprites.ContainsKey("sp_techstats2025"))
        {
            return false;
        }
        Sprite("sp_techstats2025", gimmick ? 0 : 1, x, y, Color.White, scale);
        return true;
    }

    /// <summary>
    /// 用统计数字精灵右对齐画一个整数。字距取一个源像素，与原版面板一致。
    /// 资源缺失时返回 false，由调用方退回文字。
    /// </summary>
    public bool DrawStatNumber(Session session, float rightX, float y, float scale, int value)
    {
        Use(session.GameUi);
        if (current?.Data == null || !current.Data.Sprites.TryGetValue("sp_font_techstat", out var sprite))
        {
            return false;
        }
        string text = value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var glyphs = new List<(Texture Texture, Rect Bounds)>(text.Length);
        float total = 0;
        foreach (char c in text)
        {
            if (c is < '0' or > '9' || c - '0' >= sprite.Frames.Count)
            {
                return false;
            }
            string file = sprite.Frames[c - '0'];
            if (Opaque(file, sprite) is not { } bounds)
            {
                return false;
            }
            glyphs.Add((Image(file), bounds));
            total += bounds.W * scale;
        }
        if (glyphs.Count == 0)
        {
            return false;
        }
        total += scale * (glyphs.Count - 1);
        float pen = rightX - total;
        foreach (var (texture, bounds) in glyphs)
        {
            // 只取不透明区域那一块：源图四周留白，整张贴会把数字排得过散。
            var uv = new Rect(bounds.X / texture.Width, bounds.Y / texture.Height,
                bounds.W / texture.Width, bounds.H / texture.Height);
            canvas.Quad(texture, new(pen, y, bounds.W * scale, bounds.H * scale), Color.White, uv);
            pen += (bounds.W + 1) * scale;
        }
        return true;
    }

    /// <summary>数字精灵的不透明包围盒，按文件缓存；无法读取或整张透明时返回 null。</summary>
    Rect? Opaque(string file, GameUiAssets.Sprite sprite)
    {
        if (opaqueBounds.TryGetValue(file, out var cached))
        {
            return cached;
        }
        Rect? bounds = null;
        try
        {
            using var stream = System.IO.File.OpenRead(current!.File(file));
            var image = StbImageSharp.ImageResult.FromStream(stream, StbImageSharp.ColorComponents.RedGreenBlueAlpha);
            int minX = image.Width, minY = image.Height, maxX = -1, maxY = -1;
            for (int py = 0; py < image.Height; py++)
            {
                for (int px = 0; px < image.Width; px++)
                {
                    if (image.Data[(py * image.Width + px) * 4 + 3] == 0)
                    {
                        continue;
                    }
                    minX = Math.Min(minX, px);
                    minY = Math.Min(minY, py);
                    maxX = Math.Max(maxX, px);
                    maxY = Math.Max(maxY, py);
                }
            }
            if (maxX >= minX && maxY >= minY)
            {
                bounds = new(minX, minY, maxX - minX + 1, maxY - minY + 1);
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or ArgumentException)
        {
            bounds = null;
        }
        opaqueBounds[file] = bounds;
        return bounds;
    }

    /// <summary>
    /// 会进入 application proxy 的 HUD。实机行为只有 PAUSE / Escape 与两组分数跟随 proxy；
    /// 顶部 combo、判定显示、歌曲信息与难度都不属于这一层。
    /// </summary>
    public void DrawProxyHud(Session session, double time)
    {
        Use(session.GameUi);
        if (!session.Project.GameUiEnabled || current?.Data == null)
        {
            return;
        }
        double alpha = session.Timeline.Get("uialpha", time);
        if (alpha <= 0)
        {
            return;
        }
        var white = Color.White.Alpha(alpha);
        var p = session.Project;
        string font = current.Data.Fonts.ContainsKey(p.GameUiFont) ? p.GameUiFont : "fnt_monacovs";

        // sp_gameplayoverlay2024 同时包含顶部 PAUSE 与底部固定黑栏。这里只允许游玩区进入 proxy，
        // 否则自定义 prct/prcb 把采样范围扩到底部时，歌曲信息与 footer 会被一起搬走。
        canvas.Clip(new(0, 0, 320, GameplayFooterY));
        Sprite("sp_gameplayoverlay2024", 0, 0, 0, white);
        canvas.Clip(null);

        // 原版这两块底框是常驻的，这里跟着各自的数字一起开关：设置项叫"分数显示"，只留一个空框没有意义。
        if (p.GameUiScore)
        {
            Sprite("sp_2024cc_score", 0, 259, 2, white);
            Text("fnt_credits", Whole(session.Score.AccScore(time)), 290, 1, white, white, true);
        }
        if (p.GameUiExScore)
        {
            Sprite("sp_2024cc_score", 2, 259, 18, white);
            Text(font, Whole(session.Score.ExScore(time)), 295, 19, white, white, true);
        }
        Text(font, "Escape", 38, 4, white, white);
    }

    /// <summary>
    /// 固定在 application surface 上、受 uialpha 整体控制的 HUD。底栏、歌曲信息与难度使用同一个 alpha，
    /// uialpha=0 时这一层必须完全透明，不能留下遮挡字幕/图片的黑条。
    /// </summary>
    public void DrawFixedHud(Session session, double time)
    {
        Use(session.GameUi);
        if (!session.Project.GameUiEnabled || current?.Data == null)
        {
            return;
        }

        double alpha = Math.Clamp(session.Timeline.Get("uialpha", time), 0, 1);
        if (alpha <= 0)
        {
            return;
        }

        // sp_gameplayoverlay2024 同时含顶部内容和底栏，这里只裁出固定 footer，并让它与其余 HUD 一起淡出。
        canvas.Clip(new(0, GameplayFooterY, 320, 180 - GameplayFooterY));
        Sprite("sp_gameplayoverlay2024", 0, 0, 0, Color.White.Alpha(alpha));
        canvas.Clip(null);

        var white = Color.White.Alpha(alpha);
        var p = session.Project;
        string font = current.Data.Fonts.ContainsKey(p.GameUiFont) ? p.GameUiFont : "fnt_monacovs";
        var info = session.Song;
        Text(font, info.Name, 3, 168, Color.Hex(0xFF006E).Alpha(alpha), Color.Hex(0xD800FF).Alpha(alpha));
        if (info.Artist.Length > 0)
        {
            Text(font, "/", 3 + Width(font, info.Name + " "), 168, white, white);
            Text(font, info.Artist, 3 + Width(font, info.Name + " / "), 168, Color.Hex(0xB200FF).Alpha(alpha), Color.Hex(0x1F1FFF).Alpha(alpha));
        }
        Sprite("sp_newdifficultyindicator", info.DifficultyFrame, 274, 167, white);
        Sprite("sp_newdifficultylevel", info.DifficultyFrame, 278, 171, Color.Hex(0).Alpha(.3 * alpha));
        Sprite("sp_newdifficultylevel", info.DifficultyFrame, 277, 170, white);
        // 原版数字精灵的帧序：0-18 是普通等级，"9+" 到 "17+" 接在后面，映射见 DifficultyNumberFrame。
        // 超出帧数范围的等级退回用文字画，不去猜一个不存在的帧。
        int level = DifficultyNumberFrame(info.Level);
        if (level >= 0 && level < current.Data.Sprites["sp_newdifficultynumbers"].Frames.Count)
        {
            Sprite("sp_newdifficultynumbers", level, 301, 170, Color.Hex(0).Alpha(.3 * alpha));
            Sprite("sp_newdifficultynumbers", level, 300, 169, white);
        }
        else
        {
            Text(font, info.Level, 300, 168, white, white);
        }
    }

    /// <summary>
    /// 真正的 GUI HUD：顶部 combo 与判定显示固定在屏幕上，不进入 proxy，也不受 uialpha 影响。
    /// hide_combo 是谱面侧的整组读数开关，同时隐藏 combo、判定信息及其新生成的 combo 粒子。
    /// </summary>
    public void DrawGuiHud(Session session, double time)
    {
        Use(session.GameUi);
        if (!session.Project.GameUiEnabled || current?.Data == null)
        {
            return;
        }
        var p = session.Project;
        string font = current.Data.Fonts.ContainsKey(p.GameUiFont) ? p.GameUiFont : "fnt_monacovs";
        DrawCombo(session, time, font);
        DrawJudgement(session, time, font);
    }

    /// <summary>兼容旧调用点：按新的三阶段顺序绘制完整 HUD。</summary>
    public void Draw(Session session, double time)
    {
        DrawProxyHud(session, time);
        DrawFixedHud(session, time);
        DrawGuiHud(session, time);
    }

    /// <summary>换包时会被 Use 复用，所以释放后必须保持对象可继续使用：清空缓存并把 current 置空即可。</summary>
    public void Dispose()
    {
        foreach (var t in textures.Values)
        {
            t.Dispose();
        }
        textures.Clear();
        glyphs.Clear();
        current = null;
    }

    /// <summary>
    /// 原生 sequence 的对话框。触发条件按 mod 事件在时间轴上的拍转成秒后与当前时间比较；
    /// VisibleCharacters 是逐字机打进度，跨行连续消费，因此必须按行顺序截断。
    /// </summary>
    public void DrawSequenceStory(Session session, double time)
    {
        if (session.NativeGimmick.Data?.Sequence is not { } sequence || sequence.Story.Count == 0) return;
        Use(session.GameUi); if (current?.Data == null) return;
        string font = Font(session);
        var state = NativeStoryState.Sample(sequence, time,
            trigger => session.Chart.Mods.Any(e => e.Name == trigger && session.Timeline.Bpm.Time(e.Beat) <= time), s => Width(font, s));
        DrawStoryBox(state, font);
    }

    /// <summary>
    /// Custom Episodes 的谱面内剧情。剧本只解析一次，时刻表按当前字体展开后缓存：换行依赖字体宽度，
    /// 所以换字体必须重算，否则停留时间会按另一套断行结果计时。
    /// 触发窗口由展开时的顺序决定，与谱面里 custom_episode 事件一一对应。
    /// 编辑器画区间和列台词也走这里，保证时间轴上看到的和预览里演的是同一份。
    /// </summary>
    public NativeSequenceDefinition? EpisodeSequence(Session session)
    {
        if (session.Episode is not { Playable: true } episode) return null;
        Use(session.GameUi); if (current?.Data == null) return null;
        string font = Font(session);
        if (!ReferenceEquals(episodeSource, episode) || episodeFont != font)
        {
            var triggers = session.Chart.Mods
                .Where(e => e.Name.Equals(EpisodeScript.ModName, StringComparison.OrdinalIgnoreCase))
                .Select(e => session.Timeline.Bpm.Time(e.Beat)).Order().ToArray();
            episodeSequence = episode.Expand(triggers, s => Width(font, s));
            episodeSource = episode;
            episodeFont = font;
        }
        return episodeSequence;
    }

    public void DrawEpisodeStory(Session session, double time)
    {
        if (EpisodeSequence(session) is not { } sequence) return;
        string font = Font(session);
        // 窗口名是展开时自己造的，不是谱面里的 mod 名，所以到点即已触发，无需再查谱面。
        DrawStoryBox(NativeStoryState.Sample(sequence, time, _ => true, s => Width(font, s)), font);
    }

    EpisodeScript? episodeSource;
    string? episodeFont;
    NativeSequenceDefinition? episodeSequence;

    string Font(Session session) =>
        current!.Data!.Fonts.ContainsKey(session.Project.GameUiFont) ? session.Project.GameUiFont : "fnt_monacovs";

    /// <summary>原版文字框的绘制。两条剧情来源共用，避免谱面内剧情和 sequence 剧情在版式上慢慢分叉。</summary>
    void DrawStoryBox(NativeStoryState? state, string font)
    {
        if (state == null) return;
        float y = (float)state.Y;
        Sprite("sp_dialogue", 0, 0, y, Color.White);
        int gradient = state.Speaker switch { "Saturday" => 1, "Dawn" => 7, _ => 0 };
        Sprite("sp_dialogue_grads", gradient, 0, y + 9, Color.White);
        if (state.Speaker.Length > 0)
        {
            Sprite("sp_namebox_new", 0, 0, y, Color.White);
            Text(font, state.Speaker, 43, y + 2, Color.Hex(0), Color.Hex(0), true);
            Text(font, state.Speaker, 43, y + 1, Color.White, Color.White, true);
        }
        float textY = state.Lines.Count == 1 ? 162 : 177 - state.Lines.Count * 10;
        var color = state.Thinking ? Color.Hex(0x00CDFF) : Color.White;
        int remaining = state.VisibleCharacters;
        for (int i = 0; i < state.Lines.Count && remaining > 0; i++)
        {
            string line = state.Lines[i]; int visible = Math.Min(remaining, line.Length);
            Text(font, line[..visible], 6, textY + i * 10, color, color);
            remaining -= visible;
        }
    }
}
