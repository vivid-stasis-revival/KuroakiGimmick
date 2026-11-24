// 按绝对时间重放剧情文本框、逐字显示与角色特效。

namespace KuroakiGimmick.Core;

/// <summary>可任意 seek 的 o_textbox 与 TextDrawer 重放；不接受游戏输入，也不驱动歌曲跳转。</summary>
/// <param name="Speaker">显示的说话人。</param>
/// <param name="Lines">排版后的文本行。</param>
/// <param name="Y">文本框逻辑纵坐标。</param>
/// <param name="VisibleCharacters">已显示字符数。</param>
/// <param name="Thinking">是否采用思考样式。</param>
/// <param name="Cleared">是否处于清屏状态。</param>
/// <param name="JitterCharacters">带抖动效果的字符位置。</param>
/// <param name="EffectTime">字符效果使用的时刻。</param>
/// <param name="SpeakerLeftAligned">说话人是否左对齐。</param>
/// <param name="RColorCharacters">使用循环颜色的字符位置。</param>
public sealed record NativeStoryState(string Speaker, IReadOnlyList<string> Lines, double Y, int VisibleCharacters, bool Thinking, bool Cleared,
    IReadOnlySet<int>? JitterCharacters = null, double EffectTime = 0, bool SpeakerLeftAligned = false,
    IReadOnlySet<int>? RColorCharacters = null)
{
    /// <summary>
    /// 按绝对时间还原文本框状态，不依赖上一帧，因此倒拖和导出得到同一结果。
    /// time 单位秒；Y 与换行宽度都在 320×180 逻辑空间里。
    /// </summary>
    /// <param name="sequence">定义剧情、音频和场景事件的序列。</param>
    /// <param name="time">采样时刻，单位为秒。</param>
    /// <param name="triggered">判断剧情触发键是否已生效的函数。</param>
    /// <param name="width">测量文本宽度的函数。</param>
    /// <param name="context">可选的玩家与剧情进度信息。</param>
    /// <returns>当前文本框状态，未命中时间窗时为 null。</returns>
    public static NativeStoryState? Sample(NativeSequenceDefinition sequence, double time, Func<string, bool> triggered, Func<string, float> width,
        NativeStoryContext? context = null)
    {
        var window = sequence.StoryWindows.LastOrDefault(w => w.Start <= time && (w.Destroy == null || time < w.Destroy) && triggered(w.Trigger));
        if (window == null) return null;
        // 原版的入场 tween：1 秒内从 y=180 缓动到 132，行数变化时再以当前位置为新起点重新起 tween。
        double tweenTime = window.Start, from = 180, to = 132, span = 1;
        int lineCount = 4;
        double Y(double at)
        {
            double t = span <= 0 ? 1 : Math.Clamp((at - tweenTime) / span, 0, 1);
            double eased = t >= 1 ? 1 : 1 - Math.Pow(2, -10 * t);
            return from + (to - from) * eased;
        }
        NativeSequenceDefinition.StoryCue? current = null;
        IReadOnlyList<string> lines = [];
        bool thinking = false;
        var jitterCharacters = new HashSet<int>();
        var rcolorCharacters = new HashSet<int>();
        foreach (var cue in sequence.Story.Where(c => c.Trigger == window.Trigger && c.Time >= window.Start && c.Time <= time).OrderBy(c => c.Time))
        {
            current = cue;
            if (cue.Clear) continue;
            thinking = cue.Text.StartsWith("`c{think}", StringComparison.Ordinal);
            string text = thinking ? cue.Text[9..] : cue.Text;
            if (cue.FormatPlayerName)
            {
                if (string.IsNullOrWhiteSpace(context?.PlayerName))
                {
                    current = null;
                    lines = [];
                    continue;
                }
                text = text.Replace("{0}", context.PlayerName, StringComparison.Ordinal);
            }
            var marked = Markup(text);
            lines = Wrap(marked.Text, width);
            jitterCharacters.Clear();
            rcolorCharacters.Clear();
            int sourceOffset = 0, displayOffset = 0;
            foreach (string line in lines)
            {
                int sourceStart = marked.Text.IndexOf(line, sourceOffset, StringComparison.Ordinal);
                if (sourceStart < 0) sourceStart = sourceOffset;
                for (int i = 0; i < line.Length; i++)
                {
                    if (marked.Jitter.Contains(sourceStart + i)) jitterCharacters.Add(displayOffset + i);
                    if (marked.RColor.Contains(sourceStart + i)) rcolorCharacters.Add(displayOffset + i);
                }
                sourceOffset = sourceStart + line.Length;
                displayOffset += line.Length;
            }
            if (lines.Count != lineCount)
            {
                from = Y(cue.Time);
                to = 162 - Math.Clamp(lines.Count, 2, 10) * 10;
                tweenTime = cue.Time;
                lineCount = lines.Count;
                span = 1;
            }
        }
        if (current == null) return new("", [], Y(time), 0, false, false);
        bool cleared = window.Clear is double clear && time >= clear;
        if (cleared)
        {
            // 原版剧情房间明确从 122 开始退场，短文本也一样，不按实际行数重新取起点；
            // 谱面内剧情改为从停稳位置退场、用更短的时长，由窗口自己带着这两个值。
            from = window.ExitFrom;
            to = 180;
            tweenTime = window.Clear!.Value;
            span = window.ExitSeconds;
        }
        int count = lines.Sum(s => s.Length);
        // 打字速度是每秒 100×Speed 个字；1e-8 只用来抵消浮点误差，避免整秒边界少显示一个字。
        int visible = cleared || current.Clear ? 0 : (int)Math.Clamp(Math.Floor((time - current.Time) * 100 * current.Speed + 1e-8), 0, count);
        bool leftAligned = current.Speaker == "DO_ST" && context?.StoryProgress >= 27;
        return new(ResolveSpeaker(current.Speaker, context, time), lines, Y(time), visible, thinking, cleared,
            jitterCharacters, time - current.Time, leftAligned, rcolorCharacters);
    }

    /// <summary>根据剧情上下文解析显示的说话人名称。</summary>
    /// <param name="name">脚本中的说话人名称。</param>
    /// <param name="context">可选的玩家与剧情进度信息。</param>
    /// <param name="time">采样时刻，单位为秒。</param>
    /// <returns>应用剧情上下文后的显示名。</returns>
    public static string ResolveSpeaker(string name, NativeStoryContext? context, double time)
    {
        if (name != "DO_ST") return name;
        if (context == null) return "";
        if (context.StoryProgress >= 27)
            return Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(context.SelectedTitle == "Sacrificial"
                ? "FIND ME IN THE VOID" : "THE TITLE BETRAYS YOU"));
        uint seed = unchecked((uint)(long)Math.Floor(time * 60));
        int number = 1_000_000 + Math.Min(8_999_999, (int)(Timeline.Hash(seed) * 9_000_000));
        string encoded = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(number.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        return encoded[..^2];
    }

    static readonly uint[] rcolorPalette = [16777215, 16724071, 11960319, 15564799];
    /// <summary>取得指定字符在当前剧情颜色效果下的显示色。</summary>
    /// <param name="time">采样时刻，单位为秒。</param>
    /// <param name="character">文字中的字符索引。</param>
    /// <returns>该字符在当前效果相位下的颜色。</returns>
    public static uint RColorAt(double time, int character) => rcolorPalette[Math.Min(3,
        (int)(Timeline.Hash(unchecked((uint)(long)Math.Floor(time * 4.5) * 65537 + (uint)character + 1)) * 4))];

    static (string Text, HashSet<int> Jitter, HashSet<int> RColor) Markup(string text)
    {
        var plain = new System.Text.StringBuilder();
        var jitter = new HashSet<int>();
        var rcolor = new HashSet<int>();
        string active = "normal";
        for (int i = 0; i < text.Length; i++)
        {
            if (text.AsSpan(i).StartsWith("`e{jitter}", StringComparison.Ordinal))
            {
                active = "jitter";
                i += 9;
                continue;
            }
            if (text.AsSpan(i).StartsWith("`e{rcolor}", StringComparison.Ordinal))
            {
                active = "rcolor";
                i += 9;
                continue;
            }
            if (text.AsSpan(i).StartsWith("`e{normal}", StringComparison.Ordinal))
            {
                active = "normal";
                i += 9;
                continue;
            }
            if (active == "jitter") jitter.Add(plain.Length);
            if (active == "rcolor") rcolor.Add(plain.Length);
            plain.Append(text[i]);
        }
        return (plain.ToString(), jitter, rcolor);
    }

    /// <summary>复刻 TextDrawer 的换行：在空格处向前看整个单词，宽度超过 <paramref name="limit"/> 逻辑像素才断行。</summary>
    /// <param name="text">待排版文本。</param>
    /// <param name="width">测量字符串宽度的函数。</param>
    /// <param name="limit">单行的宽度上限。</param>
    /// <returns>换行后的文本行列表。</returns>
    public static IReadOnlyList<string> Wrap(string text, Func<string, float> width, double limit = 308)
    {
        var lines = new List<string>();
        string row = "";
        for (int i = 0; i < text.Length; i++)
        {
            char ch = text[i];
            if (ch == '\n')
            {
                lines.Add(row);
                row = "";
                continue;
            }
            if (ch == ' ')
            {
                int end = i + 1;
                while (end < text.Length && text[end] != ' ' && text[end] != '\n') end++;
                // TextDrawer 的单词预读不包含字符串的最后一个字符，这里照搬该偏差以对齐原版断行位置。
                int lookEnd = end == text.Length ? Math.Max(i + 1, end - 1) : end;
                if (width(row + text[i..lookEnd]) > limit)
                {
                    lines.Add(row);
                    row = "";
                    continue;
                }
            }
            row += ch;
        }
        lines.Add(row);
        return lines;
    }
}
