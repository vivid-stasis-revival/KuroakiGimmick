using System.Buffers;
using System.Text;

namespace KuroakiGimmick.Core;

/// <summary>Bounded-buffer source reading that retains CRLF, CR and LF exactly.</summary>
internal static class SourceLines
{
    internal readonly record struct Line(string Text, string Ending);

    internal static IEnumerable<Line> Read(TextReader reader)
    {
        char[] buffer = ArrayPool<char>.Shared.Rent(64 * 1024);
        StringBuilder? pending = null;
        try
        {
            int count;
            while ((count = reader.Read(buffer, 0, buffer.Length)) != 0)
            {
                int start = 0;
                for (int i = 0; i < count; i++)
                {
                    char character = buffer[i];
                    if (character is not ('\r' or '\n')) continue;
                    string text;
                    if (pending == null) text = new string(buffer, start, i - start);
                    else
                    {
                        pending.Append(buffer, start, i - start);
                        text = pending.ToString();
                        // Do not retain the capacity of one unusually long line
                        // for the rest of a multi-gigabyte document.
                        pending = null;
                    }
                    string ending = character == '\n' ? "\n" : "\r";
                    if (character == '\r')
                    {
                        if (i + 1 < count && buffer[i + 1] == '\n') { i++; ending = "\r\n"; }
                        else if (i + 1 == count && reader.Peek() == '\n') { reader.Read(); ending = "\r\n"; }
                    }
                    start = i + 1;
                    yield return new(text, ending);
                }
                if (start < count) (pending ??= new StringBuilder()).Append(buffer, start, count - start);
            }
            if (pending is { Length: > 0 }) yield return new(pending.ToString(), "");
        }
        finally { ArrayPool<char>.Shared.Return(buffer); }
    }
}
