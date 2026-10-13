using System.Globalization;
using KuroakiGimmick.Core.Documentation;
using KuroakiGimmick.Graphics;

namespace KuroakiGimmick.UI;

internal static class ReferenceMarkdownChecks
{
    internal static int Run()
    {
        try
        {
            int checks = 0;
            void Check(bool value, string reason) { checks++; if (!value) throw new InvalidOperationException(reason); }
            string Compact(string text) => string.Concat(text.Where(c => !char.IsWhiteSpace(c)));
            var literal = ReferenceMarkdown.Inline("`0`。");
            Check(string.Concat(literal.Select(s => s.Text)) == "0。" && literal[0].Style.HasFlag(ReferenceMarkdown.Style.Code), "Inline code kept Markdown backticks");
            var bold = ReferenceMarkdown.Inline("仅来自**当前对象 Create 文件出现的赋值**。");
            Check(bold.Any(s => s.Text == "当前对象 Create 文件出现的赋值" && s.Style.HasFlag(ReferenceMarkdown.Style.Bold)), "Bold markup was not styled");
            Check(ReferenceMarkdown.Plain("obj_custom_gimmick / textX_title / shcl") == "obj_custom_gimmick / textX_title / shcl", "Identifiers were interpreted as emphasis");
            Check(ReferenceMarkdown.Plain("imgx_{id} / imgy_<id>") == "imgx_{id} / imgy_<id>", "Placeholder identifiers were interpreted as emphasis");
            Check(ReferenceMarkdown.Plain("\\*literal\\* / \\`tick\\`") == "*literal* / `tick`", "Escaped markers changed");
            Check(ReferenceMarkdown.Plain("``a ` b``") == "a ` b", "Multi-backtick code span broke");
            Check(ReferenceMarkdown.Plain("`**literal** _value_`") == "**literal** _value_", "Code span parsed emphasis");
            Check(ReferenceMarkdown.Inline("**bold *em***").Any(s => s.Text == "em" && s.Style.HasFlag(ReferenceMarkdown.Style.Bold | ReferenceMarkdown.Style.Emphasis)), "Nested bold/emphasis broke");
            Check(ReferenceMarkdown.Inline("*em **bold***").Any(s => s.Text == "bold" && s.Style.HasFlag(ReferenceMarkdown.Style.Bold | ReferenceMarkdown.Style.Emphasis)), "Nested emphasis/bold broke");
            Check(ReferenceMarkdown.Inline("~~old~~")[0].Style.HasFlag(ReferenceMarkdown.Style.Strike), "Strike markup was not styled");
            Check(ReferenceMarkdown.Plain("a<br />b &lt; c") == "a\nb < c", "Line break/entities changed");
            Check(ReferenceMarkdown.Plain("unclosed `code **bold") == "unclosed `code **bold", "Unclosed markers discarded source text");
            var cells = ReferenceMarkdown.TableCells("| `a || b` | c\\|d | `x\\|y` |")!;
            Check(cells.Length == 3 && ReferenceMarkdown.Plain(cells[0]) == "a || b" && cells[1] == "c|d" && ReferenceMarkdown.Plain(cells[2]) == "x|y", "Pipes inside code/escapes split table columns");
            Check(ReferenceMarkdown.TableCells("| a | | c |")!.Length == 3, "Empty table cell was lost");

            const string code = "  value = `literal`;\n\t// **keep** <tag> a || b\n";
            var entry = new VsmReference.Entry { Id = "fixture", Name = "Fixture", Blocks = [
                new() { Kind = "heading", Text = "**参数** 与 `value`" },
                new() { Text = "| 名称 | 默认值 | 类型 | 支持 | **读取位置** |" },
                new() { Text = "| --- | ---: | :---: | --- | --- |" },
                new() { Text = "| `gray` | `0` | 数值 | 支持 | `a || b`，**不要改写** |" },
                new() { Kind = "paragraph", Text = "`0`。" },
                new() { Kind = "code", Text = code, Language = "gml" },
                new() { Kind = "table", Columns = ["名字", "说明"], Rows = [["`prx`", "位移"]], RowLinks = ["vsm-prx"] }
            ] };
            var normalized = ReferenceMarkdown.Blocks(entry);
            Check(normalized.Count(b => b.Kind == "table") == 2, "Split paragraph table was not normalized");
            Check(normalized.First(b => b.Kind == "table").Rows[0].Length == 5, "Official-style table has wrong column count");
            Check(normalized.Single(b => b.Kind == "code").Text == code, "Code source changed during normalization");
            Check(ReferenceMarkdown.ParseBlocks("```gml\n**literal**\n```").Single().Text == "**literal**", "Fenced source was interpreted as prose");
            Check(ReferenceMarkdown.ParseBlocks("# C#").Single().Text == "C#", "Heading stripped a literal trailing hash");

            using var fonts = new Fonts();
            float Measure(string text, float size, bool weight, bool mono) => fonts.Measure(text, size, monospaced: mono, unified: true, bold: weight);
            void ValidatePage(VsmReference.Entry document, float width)
            {
                var page = ReferenceArticleLayout.Build(document, width, Measure);
                Check(float.IsFinite(page.Height) && page.Height > 0, "Invalid document height: " + document.Id);
                float previousBottom = 0;
                foreach (var item in page.Items)
                {
                    Check(item.Top >= previousBottom - .01f && item.Height > 0, "Blocks overlap: " + document.Id);
                    previousBottom = item.Top + item.Height;
                    Check(Math.Abs(item.Widths.Sum() - Math.Max(80, width)) < .05f, "Columns exceed article width: " + document.Id);
                    if (item.Kind == "code") continue;
                    Check(item.RichLines != null, "Rich text missing: " + document.Id);
                    for (int col = 0; col < item.RichLines!.Length; col++)
                    {
                        float available = item.Widths[col] - (item.Kind.StartsWith("table-", StringComparison.Ordinal) ? 24 : item.Kind == "callout" ? 28 : item.Kind == "list-item" ? 20 : 0);
                        foreach (var line in item.RichLines[col])
                        {
                            Check(float.IsFinite(line.Width) && line.Width <= available + .1f, "Wrapped line exceeds its column: " + document.Id + ": " + line.Plain);
                            Check(line.Runs.All(r => float.IsFinite(r.X + r.Width) && r.Width >= 0), "Invalid rich-text run");
                        }
                    }
                }
                Check(page.Height >= previousBottom, "Article scroll range lost the final block");
            }
            foreach (float width in new[] { 320f, 640f, 1280f }) ValidatePage(entry, width);
            var narrow = ReferenceArticleLayout.Build(entry, 320, Measure);
            var wide = ReferenceArticleLayout.Build(entry, 1280, Measure);
            Check(narrow.Items.First(i => i.Kind == "table-row").Widths.Length == 1, "Narrow five-column table was not stacked");
            Check(wide.Items.First(i => i.Kind == "table-header").Widths.Length == 5, "Wide table lost columns");
            Check(wide.Items.Any(i => i.Link == "vsm-prx") && narrow.Items.Any(i => i.Link == "vsm-prx"), "Existing table navigation target was lost");
            Check(wide.Items.Single(i => i.Kind == "code").Text == code, "Copyable code changed after wrapping");
            Check(wide.Anchors[0].Title == "参数 与 value", "Outline retains Markdown delimiters");
            var mixed = "中文 **粗体** `obj_custom_gimmick` _强调_ e\u0301 😀";
            var wrapped = ReferenceInlineLayout.Wrap(mixed, 85, 17, false, Measure);
            Check(Compact(string.Concat(wrapped.Select(l => l.Plain))) == Compact(ReferenceMarkdown.Plain(mixed)), "Wrapping lost rich-text characters");
            Check(wrapped.SelectMany(l => l.Runs).All(r => !r.Text.Any(char.IsSurrogate) || r.Text.EnumerateRunes().All(c => c.Value != 0xFFFD)), "Wrapping split a Unicode scalar");

            var catalogue = VsmReference.Shared;
            foreach (var document in catalogue.Entries)
                foreach (float width in new[] { 320f, 640f, 1280f }) ValidatePage(document, width);
            var astellion = catalogue.Find("kg-official:obj_astellion_gimmick");
            if (astellion != null)
            {
                var table = ReferenceMarkdown.Blocks(astellion).Single(b => b.Kind == "table");
                Check(table.Columns.Length == 5 && table.Rows.Length > 10, "Reported official index is still plain text");
                Check(ReferenceArticleLayout.Build(astellion, 1280, Measure).Items.Any(i => i.Kind == "table-header"), "Reported page has no rendered table");
            }
            Console.WriteLine($"PASS reference Markdown/layout: {checks} checks; inline styles, code/pipe escaping, responsive tables, source copy, row navigation, Unicode; {catalogue.Entries.Count} bundled entries at 3 widths (no SDL window)");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}
