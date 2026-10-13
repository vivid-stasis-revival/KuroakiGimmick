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
        float[] Widths, string Text = "", string Link = "", string Anchor = "", ReferenceInlineLayout.Line[][]? RichLines = null);
    internal sealed record Anchor(string Title, float Top);
    internal sealed record Page(Item[] Items, Anchor[] Anchors, float Height);
    internal const float BodySize = 17, LineHeight = 28;

    /// <summary>
    /// 排一整篇条目。measure 的三个参数为（文本, 字号, 是否粗体），返回 UI 像素宽度；所有 Top/Height 都是页内 UI 像素。
    /// 布局只依赖 availableWidth 与 measure，同样输入必得同样结果，可以直接按宽度缓存。
    /// </summary>
    internal static Page Build(VsmReference.Entry entry, float availableWidth, Func<string, float, bool, float> measure) =>
        Build(entry, availableWidth, (text, size, bold, _) => measure(text, size, bold));

    internal static Page Build(VsmReference.Entry entry, float availableWidth, Func<string, float, bool, bool, float> measure)
    {
        float width = Math.Max(80, availableWidth);
        float top = 0;
        var items = new List<Item>(); var anchors = new List<Anchor>();
        ReferenceInlineLayout.Line[] Wrap(string text, float w, float size = BodySize, bool bold = false) =>
            ReferenceInlineLayout.Wrap(text, Math.Max(1, w), size, bold, measure);
        void RichItem(string kind, float height, ReferenceInlineLayout.Line[][] lines, float[] widths, string link = "", string anchor = "") =>
            items.Add(new(kind, top, height, lines.Select(cell => cell.Select(line => line.Plain).ToArray()).ToArray(),
                widths, Link: link, Anchor: anchor, RichLines: lines));
        foreach (var block in ReferenceMarkdown.Blocks(entry))
        {
            if (block.Kind == "heading")
            {
                if (top > 0) top += 18;
                string title = ReferenceMarkdown.Plain(block.Text);
                anchors.Add(new(title, top));
                var lines = Wrap(block.Text, width, 20, true);
                float height = lines.Length * 30 + 10;
                RichItem("heading", height, [lines], [width], anchor: title);
                top += height;
            }
            else if (block.Kind == "code")
            {
                // Only presentation wraps; the Copy button always receives the exact code.
                var lines = CodeLines(block.Text, width - 32, text => measure(text, 16, false, true));
                float height = 40 + Math.Max(1, lines.Length) * 26 + 14;
                items.Add(new("code", top, height, [lines], [width], block.Text, Anchor: block.Language ?? ""));
                top += height + 20;
            }
            else if (block.Kind == "table" && block.Columns.Length > 0)
            {
                int count = block.Columns.Length;
                bool stacked = count > 1 && width < Math.Max(480, count * 135);
                float[] widths = count switch
                {
                    1 => [width],
                    2 => [width * .37f, width * .63f],
                    3 => [width * .29f, width * .31f, width * .40f],
                    5 => [width * .14f, width * .17f, width * .09f, width * .16f, width * .44f],
                    _ => Enumerable.Repeat(width / count, count).ToArray()
                };
                void Row(string[] cells, bool header, string link)
                {
                    if (stacked)
                    {
                        if (header) return;
                        var lines = new List<ReferenceInlineLayout.Line>();
                        for (int col = 0; col < count; col++)
                        {
                            var spans = ReferenceMarkdown.Inline(block.Columns[col])
                                .Select(span => span with { Style = span.Style | ReferenceMarkdown.Style.Bold })
                                .Append(new ReferenceMarkdown.Span(": ", ReferenceMarkdown.Style.Bold))
                                .Concat(ReferenceMarkdown.Inline(col < cells.Length ? cells[col] : ""));
                            lines.AddRange(ReferenceInlineLayout.Wrap(spans, width - 24, 15, false, measure));
                        }
                        float height = Math.Max(1, lines.Count) * 25 + 20;
                        RichItem("table-row", height, [lines.ToArray()], [width], link); top += height;
                    }
                    else
                    {
                        var lines = Enumerable.Range(0, count).Select(i => Wrap(i < cells.Length ? cells[i] : "", widths[i] - 24, 15, header)).ToArray();
                        float height = Math.Max(1, lines.Max(row => row.Length)) * 25 + 20;
                        RichItem(header ? "table-header" : "table-row", height, lines, widths, link); top += height;
                    }
                }
                Row(block.Columns, true, "");
                for (int i = 0; i < block.Rows.Length; i++) Row(block.Rows[i], false, i < block.RowLinks.Length ? block.RowLinks[i] : "");
                top += 24;
            }
            else if (block.Text.Length > 0)
            {
                bool note = block.Kind == "callout", list = block.Kind == "list-item";
                var lines = Wrap(block.Text, width - (note ? 28 : list ? 20 : 0));
                float height = lines.Length * LineHeight + (note ? 20 : 0);
                RichItem(note ? "callout" : list ? "list-item" : "paragraph", height, [lines], [width]);
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
