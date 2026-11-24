// 通过文本游标比较 VSM 文档内容。

namespace KuroakiGimmick.Core.Editing;

public sealed partial class VsmDocument
{
    // 脏状态检查复用已有不可变源行，逐段比较文本，避免保存整份 UTF-16 副本；分行切片不同仍可能表示相同文本。
    internal static bool TextEquals(IReadOnlyList<Line> left, IReadOnlyList<Line> right)
    {
        var a = new TextCursor(left);
        var b = new TextCursor(right);
        while (true)
        {
            var x = a.Remaining;
            var y = b.Remaining;
            if (x.IsEmpty || y.IsEmpty) return x.IsEmpty && y.IsEmpty;
            int count = Math.Min(x.Length, y.Length);
            if (!x[..count].SequenceEqual(y[..count])) return false;
            a.Advance(count);
            b.Advance(count);
        }
    }

    struct TextCursor(IReadOnlyList<Line> lines)
    {
        int line, part, offset;
        internal ReadOnlySpan<char> Remaining
        {
            get
            {
                while (line < lines.Count)
                {
                    string text = part == 0 ? lines[line].Text : lines[line].Ending;
                    if (offset < text.Length) return text.AsSpan(offset);
                    offset = 0;
                    if (part == 0) part = 1;
                    else
                    {
                        part = 0;
                        line++;
                    }
                }
                return [];
            }
        }
        internal void Advance(int count) => offset += count;
    }
}
