using KuroakiGimmick.Core;

namespace KuroakiGimmick.Graphics;

/// <summary>
/// 固定游戏 HUD 的绘制。HUD 不复制进运动代理；文字和控件优先使用原版资源，图像内的文字不再绘制一次。
/// </summary>
// 对应 cc Draw_0 的共用游戏 HUD。美术与度量全部来自用户自己导出的、未经修改的游戏资源包，
// 绝不从截图里重新描一份。
public sealed partial class GameUiRenderer : IDisposable
{
    readonly Canvas canvas;
    GameUiAssets? current;
    readonly Dictionary<string, Texture> textures = [];
    readonly Dictionary<string, Dictionary<int, GameUiAssets.Glyph>> glyphs = [];
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

    /// <summary>
    /// 固定 HUD：所有坐标都是原版 320×180 逻辑空间里的常量，不随窗口缩放改写。
    /// 时间轴的 uialpha 统一控制整块 HUD 的透明度，为 0 时直接跳过而不是画全透明四边形。
    /// </summary>
    public void Draw(Session session, double time)
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
        Sprite("sp_gameplayoverlay2024", 0, 0, 0, white);
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
        DrawCombo(session, time, alpha);
        DrawJudgement(session, time, alpha, font);
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
        int level = -1;
        // 原版数字精灵的帧序：0-18 是普通等级，"9+" 到 "17+" 接在后面，所以带加号的等级映射为 plus+10。
        // 超出帧数范围的等级退回用文字画，不去猜一个不存在的帧。
        if (info.Level.EndsWith('+') && int.TryParse(info.Level[..^1], out int plus) && plus is >= 9 and <= 17)
        {
            level = plus + 10;
        }
        else if (int.TryParse(info.Level, out int number) && number >= 0)
        {
            level = number;
        }
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
        string font = current.Data.Fonts.ContainsKey(session.Project.GameUiFont) ? session.Project.GameUiFont : "fnt_monacovs";
        var state = NativeStoryState.Sample(sequence, time,
            trigger => session.Chart.Mods.Any(e => e.Name == trigger && session.Timeline.Bpm.Time(e.Beat) <= time), s => Width(font, s));
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
