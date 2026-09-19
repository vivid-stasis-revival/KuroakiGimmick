namespace KuroakiGimmick.Core;

/// <summary>可任意 seek 的 o_textbox 与 TextDrawer 重放；不接受游戏输入，也不驱动歌曲跳转。</summary>
public sealed record NativeStoryState(string Speaker, IReadOnlyList<string> Lines, double Y, int VisibleCharacters, bool Thinking, bool Cleared)
{
    /// <summary>
    /// 按绝对时间还原文本框状态，不依赖上一帧，因此倒拖和导出得到同一结果。
    /// time 单位秒；Y 与换行宽度都在 320×180 逻辑空间里。
    /// </summary>
    public static NativeStoryState? Sample(NativeSequenceDefinition sequence, double time, Func<string, bool> triggered, Func<string, float> width)
    {
        var window = sequence.StoryWindows.LastOrDefault(w => w.Start <= time && (w.Destroy == null || time < w.Destroy) && triggered(w.Trigger));
        if (window == null) return null;
        // 原版的入场 tween：1 秒内从 y=180 缓动到 132，行数变化时再以当前位置为新起点重新起 tween。
        double tweenTime = window.Start, from = 180, to = 132;
        int lineCount = 4;
        double Y(double at)
        {
            double t = Math.Clamp(at - tweenTime, 0, 1);
            double eased = t >= 1 ? 1 : 1 - Math.Pow(2, -10 * t);
            return from + (to - from) * eased;
        }
        NativeSequenceDefinition.StoryCue? current = null;
        IReadOnlyList<string> lines = [];
        bool thinking = false;
        foreach (var cue in sequence.Story.Where(c => c.Trigger == window.Trigger && c.Time >= window.Start && c.Time <= time).OrderBy(c => c.Time))
        {
            current = cue;
            thinking = cue.Text.StartsWith("`c{think}", StringComparison.Ordinal);
            lines = Wrap(thinking ? cue.Text[9..] : cue.Text, width);
            if (lines.Count != lineCount)
            {
                from = Y(cue.Time); to = 162 - Math.Clamp(lines.Count, 2, 10) * 10;
                tweenTime = cue.Time; lineCount = lines.Count;
            }
        }
        if (current == null) return null;
        bool cleared = window.Clear is double clear && time >= clear;
        if (cleared)
        {
            // 源码明确从 122 开始退场，短文本也一样，不按实际行数重新取起点。
            from = 122; to = 180; tweenTime = window.Clear!.Value;
        }
        int count = lines.Sum(s => s.Length);
        // 原版打字速度固定每秒 100 个字符；1e-8 只用来抵消浮点误差，避免整秒边界少显示一个字。
        int visible = cleared ? 0 : (int)Math.Clamp(Math.Floor((time - current.Time) * 100 + 1e-8), 0, count);
        return new(current.Speaker, lines, Y(time), visible, thinking, cleared);
    }

    /// <summary>复刻 TextDrawer 的换行：在空格处向前看整个单词，宽度超过 308 逻辑像素才断行。</summary>
    public static IReadOnlyList<string> Wrap(string text, Func<string, float> width)
    {
        var lines = new List<string>(); string row = "";
        for (int i = 0; i < text.Length; i++)
        {
            char ch = text[i];
            if (ch == '\n') { lines.Add(row); row = ""; continue; }
            if (ch == ' ')
            {
                int end = i + 1;
                while (end < text.Length && text[end] != ' ' && text[end] != '\n') end++;
                // TextDrawer 的单词预读不包含字符串的最后一个字符，这里照搬该偏差以对齐原版断行位置。
                int lookEnd = end == text.Length ? Math.Max(i + 1, end - 1) : end;
                if (width(row + text[i..lookEnd]) > 308) { lines.Add(row); row = ""; continue; }
            }
            row += ch;
        }
        lines.Add(row); return lines;
    }
}
