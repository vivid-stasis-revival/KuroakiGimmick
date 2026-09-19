using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using KuroakiGimmick.UI;

namespace KuroakiGimmick.Core;

/// <summary>只跑 CPU 的字体度量与折行测试。由 --editor-self-test 调用；不初始化 SDL，直接读随程序分发的度量 JSON。</summary>
public static class HelpTypographySelfTest
{
    /// <summary>用 JSON 里的 Advance 自己算宽度，和渲染器同源但不碰 GPU；返回通过的检查数供上层折算。</summary>
    public static int Run()
    {
        int count = 0;
        void Check(bool value, string label)
        {
            if (!value) throw new InvalidOperationException("HELP TEST FAILED: " + label);
            count++;
            Console.WriteLine("PASS " + label);
        }
        string root = Path.Combine(Paths.Assets, "Fonts");
        using var regular = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "editor-help-sans.json")));
        using var bold = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "editor-help-sans-bold.json")));
        var metrics = regular.RootElement;
        var glyphs = metrics.GetProperty("Glyphs");
        float em = metrics.GetProperty("EmSize").GetSingle();
        // 按 em 归一化折算到目标字号，缺字回退到 "?"：这与帮助页面实际的测量口径一致，
        // 否则这里量出来的宽度和渲染出来的宽度会对不上，折行结果就失去意义。默认 17 是正文字号。
        float Width(string text, float size = 17)
        {
            float sum = 0;
            foreach (var rune in text.EnumerateRunes())
            {
                if (!glyphs.TryGetProperty(rune.ToString(), out var glyph)) glyph = glyphs.GetProperty("?");
                sum += glyph.GetProperty("Advance").GetSingle() * size / em;
            }
            return sum;
        }
        // 标题和正文必须是同一个比例字族：两个字重分别来自不同 family 会让标题与正文的字形和字重对不上。
        Check(metrics.GetProperty("Family").GetString() == "Noto Sans CJK SC" &&
            bold.RootElement.GetProperty("Family").GetString() == "Noto Sans CJK SC", "help title and body use one proportional family");
        // W 至少是 i 的两倍宽 —— 等宽字体下两者相等，这一条专门拦"帮助字体又被换回 Mono"的回退。
        Check(Width("W") > Width("i") * 2, "help Latin text is proportional, not Mono");
        // 17 号字下"中"的步进必须正好是 17：曾经的做法是按 32px 位图格子量再缩放，那样会多出半像素误差，
        // 累积到一行末尾就撑破折行宽度。这里用 .01 的容差把这条路径钉死。
        Check(Math.Abs(Width("中", 17) - 17) < .01, "17-unit CJK em is not secretly scaled through a 32px cell");
        float lineBox = (metrics.GetProperty("Ascent").GetSingle() + metrics.GetProperty("Descent").GetSingle()) * 17 / em;
        // 29 是帮助正文实际排版用的行距上限；声明的 ascent+descent 一旦超出，相邻两行就会互相啃到。
        Check(lineBox <= 29, "body line height fits the declared ascent and descent");
        // 220 / 320 / 690 对应帮助面板在窄、中、宽三种布局下的真实文本列宽。测试串故意中英数字混排，
        // 因为断行规则在 CJK 与 Latin 交界处最容易出错。
        foreach (float width in new[] { 220f, 320f, 690f })
        {
            string text = "关系：scrollspeed × velocity × scrollindN 决定主要纵向移动速度。";
            var rows = EditorHelpLayout.Wrap(text, width, s => Width(s));
            Check(rows.All(row => Width(row) <= width + .001f), $"mixed CJK/Latin lines fit width {width}");
            Check(Regex.Replace(string.Concat(rows), @"\s", "") == Regex.Replace(text, @"\s", ""),
                $"wrapping preserves all non-whitespace content at width {width}");
            Check(rows.Any(row => row.Contains("scrollspeed", StringComparison.Ordinal)),
                $"normal identifiers remain intact at width {width}");
        }
        // 组合字符（e + U+0301）与代理对（emoji）都必须整体搬运：按 char 而不是按字素簇折行时，
        // 会出现重音符号单独起一行、或者半个代理对变成豆腐块。下面两条分别查"没丢字符"和"没在边界切断字素"。
        const string unicode = "组合 e\u0301 与 \U0001F600 不拆开";
        var unicodeRows = EditorHelpLayout.Wrap(unicode, 90, s => Width(s));
        Check(string.Concat(unicodeRows).Contains("e\u0301", StringComparison.Ordinal) &&
            string.Concat(unicodeRows).Contains("\U0001F600", StringComparison.Ordinal), "wrapping preserves combining marks and surrogate pairs");
        Check(unicodeRows.All(row => !row.StartsWith('\u0301') &&
            (row.Length == 0 || !char.IsLowSurrogate(row[0]) && !char.IsHighSurrogate(row[^1]))), "no split grapheme at line boundaries");
        // 单个词就超过整行宽度时（这里是 200 个 W 拼成的图像名）必须强制断开，而不是溢出到面板外面。
        // 与上面"正常标识符保持完整"是一对：放得下就不断，放不下才断，但两种情况都不许丢字符。
        string longId = "imgx_" + new string('W', 200);
        var longRows = EditorHelpLayout.Wrap(longId, 220, s => Width(s));
        Check(string.Concat(longRows) == longId && longRows.All(row => Width(row) <= 220.001),
            "oversized identifiers wrap without losing data");
        // 标题按 22 号字量（正文是 17），省略号的截断位置也要靠实测宽度算出来：
        // 比例字体下按字符数硬截，遇到一串宽字形必然溢出标题栏。
        string fitted = EditorHelpLayout.FitTitle(longId, 300, s => Width(s, 22));
        Check(fitted.EndsWith('…') && Width(fitted, 22) <= 300.001, "oversized headings use a measured ellipsis");
        return count;
    }
}
