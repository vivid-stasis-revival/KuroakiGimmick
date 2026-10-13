using System.Globalization;
using System.Text;
using KuroakiGimmick.Core.Documentation;

namespace KuroakiGimmick.UI;

/// <summary>Wrap styled text using the same bold/monospaced metrics used to draw each run.</summary>
internal static class ReferenceInlineLayout
{
    internal sealed record Run(string Text, ReferenceMarkdown.Style Style, float X, float Width)
    {
        internal float Padding => Style.HasFlag(ReferenceMarkdown.Style.Code) ? 2 : 0;
    }
    internal sealed record Line(Run[] Runs, float Width)
    {
        internal string Plain => string.Concat(Runs.Select(r => r.Text));
    }

    static IEnumerable<string> Tokens(string text)
    {
        var word = new StringBuilder();
        var elements = StringInfo.GetTextElementEnumerator(text.Replace("\r\n", "\n", StringComparison.Ordinal));
        while (elements.MoveNext())
        {
            string element = elements.GetTextElement();
            bool part = element.Length == 1 && (char.IsAsciiLetterOrDigit(element[0]) || element[0] is '_' or '.' or '/' or '-' or '\\');
            if (part) { word.Append(element); continue; }
            if (word.Length > 0) { yield return word.ToString(); word.Clear(); }
            yield return element;
        }
        if (word.Length > 0) yield return word.ToString();
    }

    internal static Line[] Wrap(string text, float width, float size, bool bold,
        Func<string, float, bool, bool, float> measure) => Wrap(ReferenceMarkdown.Inline(text), width, size, bold, measure);

    internal static Line[] Wrap(IEnumerable<ReferenceMarkdown.Span> spans, float width, float size, bool bold,
        Func<string, float, bool, bool, float> measure)
    {
        width = Math.Max(1, width);
        var lines = new List<Line>(); var runs = new List<Run>(); float x = 0;
        void NewLine() { lines.Add(new(runs.ToArray(), x)); runs.Clear(); x = 0; }
        float Extra(ReferenceMarkdown.Style style) => runs.Count > 0 && runs[^1].Style == style ? 0 :
            (style.HasFlag(ReferenceMarkdown.Style.Code) ? 4 : 0) + (style.HasFlag(ReferenceMarkdown.Style.Emphasis) ? 3 : 0);
        float Measure(string text, ReferenceMarkdown.Style style) =>
            measure(text, size, style.HasFlag(ReferenceMarkdown.Style.Bold), style.HasFlag(ReferenceMarkdown.Style.Code));
        void Add(string value, ReferenceMarkdown.Style style)
        {
            float advance = Measure(value, style), extra = Extra(style);
            if (runs.Count > 0 && runs[^1].Style == style)
            {
                var old = runs[^1]; runs[^1] = old with { Text = old.Text + value, Width = old.Width + advance };
            }
            else runs.Add(new(value, style, x, advance + extra));
            x += advance + extra;
        }
        foreach (var span in spans)
        {
            var style = span.Style | (bold ? ReferenceMarkdown.Style.Bold : ReferenceMarkdown.Style.Plain);
            foreach (string token in Tokens(span.Text))
            {
                if (token == "\n") { NewLine(); continue; }
                bool space = string.IsNullOrWhiteSpace(token);
                if (space && x == 0) continue;
                if (x > 0 && x + Measure(token, style) + Extra(style) > width)
                { NewLine(); if (space) continue; }
                if (Measure(token, style) + Extra(style) <= width) { Add(token, style); continue; }
                // Very long identifiers/code spans break only at grapheme boundaries.
                var elements = StringInfo.GetTextElementEnumerator(token);
                while (elements.MoveNext())
                {
                    string element = elements.GetTextElement();
                    if (x > 0 && x + Measure(element, style) + Extra(style) > width) NewLine();
                    Add(element, style);
                }
            }
        }
        if (runs.Count > 0 || lines.Count == 0) NewLine();
        return lines.ToArray();
    }
}
