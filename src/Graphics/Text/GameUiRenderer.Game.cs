using System.Globalization;
using KuroakiGimmick.Core;

namespace KuroakiGimmick.Graphics;

/// <summary>
/// Combo / judgement 等固定 GUI HUD。它们不进入 application proxy，并保持各自独立的显隐语义。
/// </summary>
// 对应 o_combodisplay、o_judgement_ingame 与 obj_judgement_display 三个独立对象。
// 它们在原版里各自带状态（补间进度、上次判定、淡出计时），这里统一改写成"距上次判定多久"的闭式，
// 因为预览要支持拖时间轴：任何跨帧累加的量在跳转后都会停在错误的位置。
public sealed partial class GameUiRenderer
{
    /// <summary>原版 font_add_sprite(sp_combofont_newer, 32, true, -7)：首帧对应空格，字距是负的，字形描边有意互相压边。</summary>
    const string ComboFont = "sp_combofont_newer";
    const int ComboFirstChar = 32, ComboSeparation = -7;
    /// <summary>房间里 o_combodisplay 的 y，也就是补间的终点 ystart；+3 是每次判定弹下去的幅度。</summary>
    const float ComboY = 4, ComboBaseline = 7;
    /// <summary>原版 CreateUiTween(0, 1, ..., ystart + 3, ystart) 与判定显示的 0.5 秒缩放补间。</summary>
    const double ComboTween = 1, JudgementTween = .5;
    /// <summary>原版 o_judgement_ingame 的房间坐标；经典样式的 ystart 会从 27 补间回 24。</summary>
    const float JudgementX = 160, JudgementY = 24, JudgementClassicPop = 27;
    /// <summary>现代样式改缩放而不改位置，从 1.125 收回 1。</summary>
    const float JudgementPopScale = 1.125f;
    /// <summary>obj_judgement_display 的 alph 从 13 起按曲目时间每秒掉 6：前 2 秒都被夹在 1，之后 1/6 秒淡光。</summary>
    const double JudgementTextAlpha = 13, JudgementTextFade = 6;

    /// <summary>判定文本的字面与配色，下标即判定档位。原版常量是 BGR 字节序，这里换算成 RGB。</summary>
    static readonly string[] judgementNames = ["A.CRITICAL", "CRITICAL", "GREAT", "GOOD", "FAILED"];
    static readonly Color[] judgementTextColors =
    [
        Color.Hex(0xFFA100),
        Color.Hex(0xFFE049),
        Color.Hex(0x00FF21),
        Color.Hex(0x00FFFF),
        Color.Hex(0xFF0000)
    ];

    /// <summary>精灵字模的排版结果。Advance 等于原版 string_width；Left/Right 是相对起笔点的墨迹边界。</summary>
    readonly record struct SpriteTextMetrics(float Advance, float Left, float Right);

    /// <summary>
    /// 量一串精灵字模的宽度。步进 = 该帧裁剪矩形的宽 + 字距，与 GameMaker 比例精灵字体一致。
    /// 比例精灵字体将每帧裁剪区域的左边贴到笔尖，不保留帧内水平留白；
    /// 度量与 SpriteText 使用同一规则，供调用方按视觉中心对齐。
    /// 精灵缺失或没带裁剪矩形时返回 null：没有逐帧包围盒就还原不出比例字宽，宁可不画也不拿整帧宽度去猜。
    /// </summary>
    SpriteTextMetrics? Measure(string name, string text)
    {
        if (current?.Data == null || !current.Data.Sprites.TryGetValue(name, out var s) || s.Bounds is not { } bounds)
        {
            return null;
        }
        float pen = 0, left = float.MaxValue, right = float.MinValue;
        foreach (char c in text)
        {
            int frame = c - ComboFirstChar;
            if (frame < 0 || frame >= bounds.Count)
            {
                continue;
            }
            var b = bounds[frame];
            if (c != ' ')
            {
                left = Math.Min(left, pen);
                right = Math.Max(right, pen + b[2]);
            }
            pen += b[2] + ComboSeparation;
        }
        return right < left ? null : new(pen, left, right);
    }

    /// <summary>比例字模只画裁剪区域；X 不使用精灵原点和帧内留白，Y 保留原版基线偏移。</summary>
    void SpriteText(string name, string text, float penX, float penY, Color colour)
    {
        if (current?.Data == null || !current.Data.Sprites.TryGetValue(name, out var s) || s.Bounds is not { } bounds)
        {
            return;
        }
        foreach (char c in text)
        {
            int frame = c - ComboFirstChar;
            if (frame < 0 || frame >= bounds.Count)
            {
                continue;
            }
            var b = bounds[frame];
            if (c != ' ')
            {
                var texture = Image(s.Frames[frame]);
                canvas.Quad(texture, new(penX, penY + b[1] - s.OriginY, b[2], b[3]), colour,
                    new(b[0] / (float)s.Width, b[1] / (float)s.Height, b[2] / (float)s.Width, b[3] / (float)s.Height));
            }
            penX += b[2] + ComboSeparation;
        }
    }

    /// <summary>原版 o_combodisplay 的 event_user(2)：顶部大号数字按 op_minusscore 切换内容。</summary>
    public static string ComboText(Session session, double time)
    {
        double countdown = session.Timeline.Get("df_countdown", time);
        if (countdown > 0) return countdown.ToString("0.00", CultureInfo.InvariantCulture).PadLeft(5);
        var score = session.Score;
        return session.Project.GameUiCombo switch
        {
            1 => score.Combo(time).ToString(CultureInfo.InvariantCulture),
            2 => score.GameScore(time).ToString(CultureInfo.InvariantCulture),
            3 => Whole(score.CurrentScore(time)),
            4 => Whole(ScoreState.MaxScore),
            // string_format(value, 3, 2) 的整数位补空格，而 100 本来就是三位，结果就是 "100.00"。
            5 => ScoreState.Accuracy.ToString("0.00", CultureInfo.InvariantCulture) + "%",
            _ => ""
        };
    }

    /// <summary>原版 string(round(x))：GameMaker 的 round 在 .5 处取偶，与 Math.Round 的默认行为一致。</summary>
    static string Whole(double value) => Math.Round(value).ToString(CultureInfo.InvariantCulture);
    /// <summary>距上次判定过了多久（秒）。还没有任何判定时返回正无穷，各处补间因此停在终点、淡出停在 0。</summary>
    static double SinceHit(Session session, double time) => session.Score.LastHit(time) is { } hit ? time - hit : double.PositiveInfinity;
    static float ComboBounce(Session session, double time) => (float)(3 * (1 - Easings.Eval("outQuint", SinceHit(session, time) / ComboTween)));

    /// <summary>
    /// 顶部大号连击数。原版 draw_text_o(171, y + 8) 配 fa_center，但同一次事件里的钻尘是以 160 为中心向两侧分开的，
    /// 说明这串数字视觉上是落在 160 上的；这里直接按墨迹范围居中到 160，不去复刻 GameMaker 文字引擎的起笔偏移。
    /// y 的补间原版跟的是真实帧时间，这里换成曲目时间，否则拖动时间轴后数字会卡在补间半路。
    /// </summary>
    void DrawCombo(Session session, double time, string font)
    {
        if (session.Project.GameUiCombo <= 0 || session.Timeline.Get("hide_combo", time) != 0)
        {
            return;
        }
        string text = ComboText(session, time);
        if (text.Length == 0 || Measure(ComboFont, text) is not { } m)
        {
            return;
        }
        double y = ComboY + ComboBounce(session, time);
        SpriteText(ComboFont, text, 160 - (m.Left + m.Right) / 2, (float) y + ComboBaseline, Color.White);
        if (session.Timeline.Get("df_countdown", time) > 0)
            Text(font, "COUNTDOWN", 160, (float)y + 19, Color.White, Color.White, true);
    }

    /// <summary>
    /// 判定显示。原版把它拆成两个对象：o_judgement_ingame 画精灵（现代/经典两套），obj_judgement_display 画底部描边文字。
    /// 这里合并成一个四档设置，因为预览面板放不下两个独立开关。
    /// 精灵档位在首次判定前是空白帧（image_index = arg0 + 1，第 0 帧是空的），所以没有判定时直接不画。
    /// </summary>
    void DrawJudgement(Session session, double time, string font)
    {
        int mode = session.Project.GameUiJudgement;
        if (mode <= 0 || session.Timeline.Get("hide_combo", time) != 0 || session.Score.LastHit(time) == null)
        {
            return;
        }
        double age = SinceHit(session, time), ease = Easings.Eval("outQuint", age / JudgementTween);
        var white = Color.White;
        // 标题随数字弹动；判定同步避让，防止命中瞬间标题压到放大的现代判定上。
        float countdownOffset = session.Timeline.Get("df_countdown", time) > 0 ? 11 + ComboBounce(session, time) : 0;
        switch (mode)
        {
            case 1:
                // 现代样式：整块精灵以原点为中心从 1.125 倍收回 1 倍。
                Sprite("sp_judgements_2", ScoreState.Tier + 1, JudgementX, JudgementY + countdownOffset, white, (float)(JudgementPopScale + (1 - JudgementPopScale) * ease));
                break;
            case 2:
                // 经典样式：不缩放，改成从 27 抬回 24。
                Sprite("sp_judgements", ScoreState.Tier + 1, JudgementX, (float)(JudgementClassicPop + (JudgementY - JudgementClassicPop) * ease) + countdownOffset, white);
                break;
            default:
                // 底部描边文字：alph 是从 13 开始按曲目时间掉的，画的时候被 draw_set_alpha 夹在 1，
                // 所以看起来是"停 2 秒再瞬间淡掉"。draw_text_outlined 先 y - 4，draw_text_o 再 y - 1。
                double fade = Math.Clamp(JudgementTextAlpha - JudgementTextFade * age, 0, 1);
                if (fade <= 0)
                {
                    return;
                }
                string text = judgementNames[ScoreState.Tier] + " " + session.Score.Combo(time).ToString(CultureInfo.InvariantCulture);
                var outline = Color.Hex(0).Alpha(fade);
                foreach ((int dx, int dy) in new[] { (-1, -1), (0, -1), (1, -1), (-1, 1), (0, 1), (1, 1), (-1, 0), (1, 0) })
                {
                    Text(font, text, 160 + dx, 145 + dy, outline, outline, true);
                }
                var colour = judgementTextColors[ScoreState.Tier].Alpha(fade);
                Text(font, text, 160, 145, colour, colour, true);
                break;
        }
    }

    /// <summary>
    /// 每次判定时从连击数两侧向外喷的钻尘落点：原版 160 ± (string_width(combo_text) / 2 + 6)。
    /// 粒子本身由 SceneRenderer 画（钻尘纹理在那边），这里只负责把宽度算出来；数字不显示时返回 null。
    /// </summary>
    public float? ComboDustOffset(Session session, double time)
    {
        Use(session.GameUi);
        if (!session.Project.GameUiEnabled || session.Project.GameUiCombo <= 0 || current?.Data == null
            || session.Timeline.Get("hide_combo", time) != 0)
        {
            return null;
        }
        string text = ComboText(session, time);
        return text.Length == 0 || Measure(ComboFont, text) is not { } m ? null : m.Advance / 2 + 6;
    }
}
