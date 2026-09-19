using System.Globalization;
using System.Text;

namespace KuroakiGimmick.UI;

/// <summary>
/// 帮助卡片的按词 / 按字素折行。只产出显示用的行，绝不改写源帮助文本或 VSM 标识符本身。
/// </summary>
internal static class EditorHelpLayout
{
    const string ClosingPunctuation = "，。；：！？、）》】」』〉,.;:!?)]}";
    const string OpeningPunctuation = "（《【「『〈([{“‘";

    /// <summary>按宽度折行；width 与 measure 的返回值同为 UI 像素。宽度非有限或非正时原样返回单行，不做任何切分。</summary>
    internal static string[] Wrap(string text, float width, Func<string, float> measure)
    {
        if (!float.IsFinite(width) || width <= 0) return [text];
        var lines = new List<string>();
        foreach (string paragraph in text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            string line = "";
            foreach (string token in Tokens(paragraph))
            {
                if (string.IsNullOrWhiteSpace(token))
                {
                    if (line.Length > 0 && !line.EndsWith(' ')) line += " ";
                    continue;
                }
                if (measure(line + token) <= width)
                {
                    line += token;
                    continue;
                }
                if (line.Length > 0)
                {
                    // 把行尾的开引号 / 开括号，或收尾标点之前的最后一个字符，一并挪到下一行，
                    // 避免标点孤零零地落在行边界上。
                    var elements = Elements(line.TrimEnd());
                    string carry = "";
                    if (elements.Count > 1 && (OpeningPunctuation.Contains(elements[^1], StringComparison.Ordinal) ||
                        ClosingPunctuation.Contains(token[..1], StringComparison.Ordinal)))
                    {
                        carry = elements[^1];
                        elements.RemoveAt(elements.Count - 1);
                    }
                    lines.Add(string.Concat(elements).TrimEnd());
                    line = carry;
                }
                // 超长的原始标识符可能比整张卡片还宽。只有这种情况才切分 token，
                // 且只在字素边界处切，保证 UTF-16 代理对不会被拦腰截断。
                foreach (string element in Elements(token))
                {
                    if (line.Length > 0 && measure(line + element) > width)
                    {
                        lines.Add(line.TrimEnd());
                        line = "";
                    }
                    line += element;
                }
            }
            if (line.TrimEnd().Length > 0 || paragraph.Length == 0) lines.Add(line.TrimEnd());
        }
        return lines.Count == 0 ? [""] : lines.ToArray();
    }

    /// <summary>单行标题截断，尾部补省略号；按字素裁剪，不会切开代理对。宽度连省略号都放不下时返回空串。</summary>
    internal static string FitTitle(string text, float width, Func<string, float> measure)
    {
        if (measure(text) <= width) return text;
        const string ellipsis = "…";
        if (measure(ellipsis) > width) return "";
        string fitted = "";
        foreach (string element in Elements(text))
        {
            if (measure(fitted + element + ellipsis) > width) break;
            fitted += element;
        }
        return fitted + ellipsis;
    }

    /// <summary>切词：ASCII 字母数字与 _ . / - 聚成一个不可分的 token，这样 VSM 标识符和路径不会被从中间拆开。</summary>
    static IEnumerable<string> Tokens(string text)
    {
        var word = new StringBuilder();
        foreach (string element in Elements(text))
        {
            bool wordElement = element.Length == 1 &&
                (char.IsAsciiLetterOrDigit(element[0]) || element[0] is '_' or '.' or '/' or '-');
            if (wordElement) { word.Append(element); continue; }
            if (word.Length > 0) { yield return word.ToString(); word.Clear(); }
            yield return element;
        }
        if (word.Length > 0) yield return word.ToString();
    }

    /// <summary>按字素簇拆分，而不是按 char；组合字符与代理对始终留在同一个元素里。</summary>
    static List<string> Elements(string text)
    {
        var result = new List<string>();
        var enumerator = StringInfo.GetTextElementEnumerator(text);
        while (enumerator.MoveNext()) result.Add(enumerator.GetTextElement());
        return result;
    }
}
