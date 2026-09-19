using System.Globalization;
using System.Text;
using KuroakiGimmick.Core.Documentation;

namespace KuroakiGimmick.UI;

/// <summary>
/// 文档条目的排版结果（已量测、可缓存）。行只是呈现层产物；源码块保留原文供复制，不因折行而被改写。
/// </summary>
internal static class ReferenceArticleLayout
{
    /// <summary>
    /// 一个已排好版的块。Kind 是绘制端分派用的标签，取值为 heading / code / table-header / table-row /
    /// callout / list-item / paragraph。Lines 按列分组（只有表格会多于一列），Widths 与之一一对应。
    /// Text 保留源码块原文，供复制使用。
    /// </summary>
    internal sealed record Item(string Kind, float Top, float Height, string[][] Lines,
        float[] Widths, string Text = "", string Link = "", string Anchor = "");
    internal sealed record Anchor(string Title, float Top);
    internal sealed record Page(Item[] Items, Anchor[] Anchors, float Height);
    internal const float BodySize = 17, LineHeight = 28;

    /// <summary>
    /// 排一整篇条目。measure 的三个参数为（文本, 字号, 是否粗体），返回 UI 像素宽度；所有 Top/Height 都是页内 UI 像素。
    /// 布局只依赖 availableWidth 与 measure，同样输入必得同样结果，可以直接按宽度缓存。
    /// </summary>
    internal static Page Build(VsmReference.Entry entry, float availableWidth, Func<string, float, bool, float> measure)
    {
        float width = Math.Max(80, availableWidth);
        float top = 0;
        var items = new List<Item>();
        var anchors = new List<Anchor>();
        string[] Wrap(string text, float w, float size = BodySize, bool bold = false) =>
            EditorHelpLayout.Wrap(text, Math.Max(1, w), s => measure(s, size, bold));
        foreach (var block in entry.Blocks)
        {
            if (block.Kind == "heading")
            {
                if (top > 0) top += 18;
                anchors.Add(new(block.Text, top));
                var lines = Wrap(block.Text, width, 20, true);
                float height = lines.Length * 30 + 10;
                items.Add(new("heading", top, height, [lines], [width], Anchor: block.Text));
                top += height;
            }
            else if (block.Kind == "code")
            {
                // 保留换行与缩进；代码超宽时折行显示，绝不截断 —— 截断会让复制出去的示例不可用。
                var lines = CodeLines(block.Text, width - 32, s => measure(s, 16, false));
                float height = 40 + Math.Max(1, lines.Length) * 26 + 14;
                items.Add(new("code", top, height, [lines], [width], block.Text));
                top += height + 20;
            }
            else if (block.Kind == "table" && block.Columns.Length > 0)
            {
                int count = block.Columns.Length;
                float[] widths = count switch
                {
                    1 => [width],
                    2 => [width * .37f, width * .63f],
                    3 => [width * .29f, width * .31f, width * .40f],
                    _ => Enumerable.Repeat(width / count, count).ToArray()
                };
                void Row(string[] cells, bool header, string link)
                {
                    var lines = Enumerable.Range(0, count).Select(i => Wrap(i < cells.Length ? cells[i] : "",
                        widths[i] - 24, 15, header)).ToArray();
                    float height = Math.Max(1, lines.Max(row => row.Length)) * 25 + 20;
                    items.Add(new(header ? "table-header" : "table-row", top, height, lines, widths, Link: link));
                    top += height;
                }
                Row(block.Columns, true, "");
                for (int i = 0; i < block.Rows.Length; i++)
                    Row(block.Rows[i], false, i < block.RowLinks.Length ? block.RowLinks[i] : "");
                top += 24;
            }
            else if (block.Text.Length > 0)
            {
                bool note = block.Kind == "callout", list = block.Kind == "list-item";
                var lines = Wrap(block.Text, width - (note ? 28 : list ? 20 : 0));
                float height = lines.Length * LineHeight + (note ? 20 : 0);
                items.Add(new(note ? "callout" : list ? "list-item" : "paragraph", top, height, [lines], [width]));
                top += height + (list ? 8 : 16);
            }
        }
        if (anchors.Count == 0) anchors.Add(new(entry.Name, 0));
        return new(items.ToArray(), anchors.ToArray(), Math.Max(1, top));
    }

    /// <summary>只做视觉折行。保留空格并按字素切分（代理对不会被截断）；复制走的是未经改动的 Text。</summary>
    internal static string[] CodeLines(string code, float width, Func<string, float> measure)
    {
        var result = new List<string>();
        foreach (string sourceLine in code.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            string line = sourceLine.Replace("\t", "    ", StringComparison.Ordinal);
            var buffer = new StringBuilder();
            var elements = StringInfo.GetTextElementEnumerator(line);
            while (elements.MoveNext())
            {
                string element = elements.GetTextElement();
                if (buffer.Length > 0 && measure(buffer.ToString() + element) > Math.Max(1, width))
                { result.Add(buffer.ToString()); buffer.Clear(); }
                buffer.Append(element);
            }
            result.Add(buffer.ToString());
        }
        return result.ToArray();
    }

}
