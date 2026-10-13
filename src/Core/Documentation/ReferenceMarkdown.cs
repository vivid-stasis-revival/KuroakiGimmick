using System.Net;
using System.Text;

namespace KuroakiGimmick.Core.Documentation;

/// <summary>Presentation-only Markdown normalization. Never rewrites source excerpts or code blocks.</summary>
internal static class ReferenceMarkdown
{
    [Flags] internal enum Style { Plain = 0, Bold = 1, Emphasis = 2, Code = 4, Strike = 8 }
    internal sealed record Span(string Text, Style Style);
    const string Escapable = "\\`*_{}[]()<>#+-.!|~";

    internal static Span[] Inline(string source) => ParseInline(source, Style.Plain, 0).ToArray();
    internal static string Plain(string source) => string.Concat(Inline(source).Select(s => s.Text));

    static int Ticks(string text, int at)
    {
        int end = at;
        while (end < text.Length && text[end] == '`') end++;
        return end - at;
    }
    static int ClosingTicks(string text, int start, int count)
    {
        for (int i = start; i < text.Length; i++)
            if (text[i] == '`')
            {
                int run = Ticks(text, i);
                if (run == count) return i;
                i += run - 1;
            }
        return -1;
    }
    static int ClosingDelimiter(string text, int start, string delimiter)
    {
        for (int i = start; i <= text.Length - delimiter.Length; i++)
        {
            if (text[i] == '\\') { i++; continue; }
            if (text[i] == '`')
            {
                int ticks = Ticks(text, i), end = ClosingTicks(text, i + ticks, ticks);
                if (end >= 0) { i = end + ticks - 1; continue; }
            }
            if (text[i] != delimiter[0]) continue;
            int run = 1;
            while (i + run < text.Length && text[i + run] == delimiter[0]) run++;
            bool closes = run >= delimiter.Length && (delimiter.Length != 1 || run % 2 == 1) &&
                i > start && !char.IsWhiteSpace(text[i - 1]);
            if (delimiter[0] == '_' && i + run < text.Length && char.IsLetterOrDigit(text[i - 1]) && char.IsLetterOrDigit(text[i + run])) closes = false;
            if (closes) return i + run - delimiter.Length;
            i += run - 1;
        }
        return -1;
    }
    static List<Span> ParseInline(string text, Style inherited, int depth)
    {
        var result = new List<Span>(); var plain = new StringBuilder();
        void Flush()
        {
            if (plain.Length == 0) return;
            result.Add(new(WebUtility.HtmlDecode(plain.ToString()), inherited)); plain.Clear();
        }
        for (int i = 0; i < text.Length;)
        {
            char c = text[i];
            if (c == '\\' && i + 1 < text.Length && Escapable.Contains(text[i + 1]))
            { plain.Append(text[i + 1]); i += 2; continue; }
            if (c == '`')
            {
                int run = Ticks(text, i), end = ClosingTicks(text, i + run, run);
                if (end >= 0)
                {
                    Flush();
                    string code = text[(i + run)..end].Replace('\r', ' ').Replace('\n', ' ');
                    if (code.Length > 1 && code[0] == ' ' && code[^1] == ' ' && code.Any(ch => ch != ' ')) code = code[1..^1];
                    result.Add(new(code, inherited | Style.Code)); i = end + run; continue;
                }
                plain.Append('`', run); i += run; continue;
            }
            if (c == '<')
            {
                int end = text.IndexOf('>', i + 1);
                if (end >= 0 && text[(i + 1)..end].Replace(" ", "").TrimEnd('/').Equals("br", StringComparison.OrdinalIgnoreCase))
                { plain.Append('\n'); i = end + 1; continue; }
            }
            if (depth < 16 && c is '*' or '_' or '~')
            {
                int run = 1;
                while (i + run < text.Length && text[i + run] == c && run < 3) run++;
                if (c == '~') run = run >= 2 ? 2 : 0;
                bool insideWord = c == '_' && i > 0 && char.IsLetterOrDigit(text[i - 1]);
                if (run > 0 && !insideWord && i + run < text.Length && !char.IsWhiteSpace(text[i + run]))
                {
                    string delimiter = new(c, run); int end = ClosingDelimiter(text, i + run, delimiter);
                    if (end >= 0)
                    {
                        Flush();
                        Style added = c == '~' ? Style.Strike : run == 3 ? Style.Bold | Style.Emphasis : run == 2 ? Style.Bold : Style.Emphasis;
                        result.AddRange(ParseInline(text[(i + run)..end], inherited | added, depth + 1));
                        i = end + run; continue;
                    }
                }
            }
            plain.Append(c); i++;
        }
        Flush(); return result;
    }

    /// <summary>Pipes inside code and escaped pipes belong to the cell, including GML's || operator.</summary>
    internal static string[]? TableCells(string row)
    {
        string text = row.Trim(); var cells = new List<string>(); var cell = new StringBuilder();
        bool delimiter = false;
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == '\\' && i + 1 < text.Length)
            {
                // Unescape a table pipe even inside code; preserve other backslashes verbatim.
                if (text[i + 1] == '|') { cell.Append('|'); i++; continue; }
                cell.Append(text[i]); cell.Append(text[++i]); continue;
            }
            if (text[i] == '`')
            {
                int run = Ticks(text, i), end = ClosingTicks(text, i + run, run);
                if (end >= 0)
                {
                    cell.Append(text[i..(end + run)].Replace("\\|", "|", StringComparison.Ordinal));
                    i = end + run - 1; continue;
                }
            }
            if (text[i] == '|') { delimiter = true; cells.Add(cell.ToString().Trim()); cell.Clear(); }
            else cell.Append(text[i]);
        }
        cells.Add(cell.ToString().Trim());
        if (!delimiter) return null;
        if (text.StartsWith('|')) cells.RemoveAt(0);
        if (text.EndsWith('|') && cells.Count > 0 && cells[^1].Length == 0) cells.RemoveAt(cells.Count - 1);
        return cells.ToArray();
    }

    static bool Separator(string[]? cells, int count) => cells is { Length: > 0 } && cells.Length == count && cells.All(c =>
    {
        string value = c.Trim().Trim(':');
        return value.Length >= 3 && value.All(ch => ch == '-');
    });

    internal static VsmReference.Block[] Blocks(VsmReference.Entry entry)
    {
        var source = entry.Blocks.Length > 0 ? entry.Blocks :
            [new VsmReference.Block { Text = string.Join("\n\n", entry.Body) }];
        var result = new List<VsmReference.Block>();
        for (int i = 0; i < source.Length;)
        {
            if (source[i].Kind != "paragraph") { result.Add(source[i++]); continue; }
            var text = new StringBuilder(); string previous = "";
            while (i < source.Length && source[i].Kind == "paragraph")
            {
                string current = source[i++].Text;
                // Generated indexes store each table row in a separate paragraph block.
                if (text.Length > 0) text.Append(TableCells(previous.Split('\n')[^1]) != null && TableCells(current.Split('\n')[0]) != null ? "\n" : "\n\n");
                text.Append(current); previous = current;
            }
            result.AddRange(ParseBlocks(text.ToString()));
        }
        return result.ToArray();
    }

    internal static IEnumerable<VsmReference.Block> ParseBlocks(string text)
    {
        string[] lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');
        for (int i = 0; i < lines.Length;)
        {
            string line = lines[i].Trim();
            if (line.Length == 0) { i++; continue; }
            if (line.StartsWith("```", StringComparison.Ordinal) || line.StartsWith("~~~", StringComparison.Ordinal))
            {
                char marker = line[0]; int count = line.TakeWhile(c => c == marker).Count();
                string language = line[count..].Trim(); var code = new List<string>(); i++;
                while (i < lines.Length)
                {
                    string closing = lines[i].Trim();
                    if (closing.Length >= count && closing.All(c => c == marker)) { i++; break; }
                    code.Add(lines[i++]);
                }
                yield return new() { Kind = "code", Text = string.Join("\n", code), Language = language }; continue;
            }
            var columns = TableCells(line);
            if (columns is { Length: > 0 } && i + 1 < lines.Length && Separator(TableCells(lines[i + 1]), columns.Length))
            {
                var rows = new List<string[]>(); i += 2;
                while (i < lines.Length && TableCells(lines[i]) is { } cells)
                {
                    // Keep excess data in the last cell instead of silently dropping source text.
                    if (cells.Length > columns.Length)
                        cells = cells.Take(columns.Length - 1).Append(string.Join(" | ", cells.Skip(columns.Length - 1))).ToArray();
                    rows.Add(cells); i++;
                }
                yield return new() { Kind = "table", Columns = columns, Rows = rows.ToArray() }; continue;
            }
            int hashes = line.TakeWhile(c => c == '#').Count();
            if (hashes is >= 1 and <= 6 && hashes < line.Length && char.IsWhiteSpace(line[hashes]))
            {
                string title = line[(hashes + 1)..].Trim(); int end = title.Length;
                while (end > 0 && title[end - 1] == '#') end--;
                if (end > 0 && char.IsWhiteSpace(title[end - 1])) title = title[..end].TrimEnd();
                yield return new() { Kind = "heading", Text = title };
            }
            else if (line.StartsWith(">", StringComparison.Ordinal))
                yield return new() { Kind = "callout", Text = line[1..].TrimStart() };
            else if (line.Length >= 2 && line[0] is '-' or '*' or '+' && char.IsWhiteSpace(line[1]))
                yield return new() { Kind = "list-item", Text = line[2..].TrimStart() };
            else yield return new() { Kind = "paragraph", Text = lines[i] };
            i++;
        }
    }
}
