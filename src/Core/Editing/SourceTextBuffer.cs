namespace KuroakiGimmick.Core.Editing;

/// <summary>源码草稿：保留原换行，光标/选择和局部撤销不触碰工程文档。</summary>
public sealed class SourceTextBuffer
{
    sealed record Edit(int Start, string Before, string After, int Cursor, int Anchor);
    readonly List<Edit> undo = [], redo = [];
    int[]? starts;
    public string Text { get; private set; }
    public string Original { get; }
    public string NewLine { get; }
    public int Cursor { get; private set; }
    public int Anchor { get; private set; }
    public bool Dirty => Text != Original;
    public int SelectionStart => Math.Min(Cursor, Anchor);
    public int SelectionLength => Math.Abs(Cursor - Anchor);
    public string Selection => Text.Substring(SelectionStart, SelectionLength);
    public int[] LineStarts
    {
        get
        {
            if (starts != null) return starts;
            var result = new List<int> { 0 };
            for (int i = 0; i < Text.Length; i++)
            {
                if (Text[i] == '\r' && i + 1 < Text.Length && Text[i + 1] == '\n') i++;
                else if (Text[i] is not ('\r' or '\n')) continue;
                result.Add(i + 1);
            }
            return starts = result.ToArray();
        }
    }
    public int LineAt(int position)
    {
        int at = Array.BinarySearch(LineStarts, Math.Clamp(position, 0, Text.Length));
        return at >= 0 ? at : ~at - 1;
    }
    public int LineEnd(int line)
    {
        int end = line + 1 < LineStarts.Length ? LineStarts[line + 1] : Text.Length;
        while (end > LineStarts[line] && Text[end - 1] is '\r' or '\n') end--;
        return end;
    }
    public SourceTextBuffer(string text)
    { Text = Original = text; NewLine = text.Contains("\r\n") ? "\r\n" : text.Contains('\r') ? "\r" : "\n"; }
    public void Select(int position, bool extend = false)
    {
        Cursor = Math.Clamp(position, 0, Text.Length);
        if (Cursor > 0 && Cursor < Text.Length && char.IsLowSurrogate(Text[Cursor]) && char.IsHighSurrogate(Text[Cursor - 1])) Cursor--;
        if (!extend) Anchor = Cursor;
    }
    public void SelectAll() { Anchor = 0; Cursor = Text.Length; }
    int Previous(int at) => at <= 0 ? 0 : at >= 2 &&
        (char.IsLowSurrogate(Text[at - 1]) && char.IsHighSurrogate(Text[at - 2]) || Text[at - 1] == '\n' && Text[at - 2] == '\r') ? at - 2 : at - 1;
    int Next(int at) => at >= Text.Length ? Text.Length : at + 1 < Text.Length &&
        (char.IsHighSurrogate(Text[at]) && char.IsLowSurrogate(Text[at + 1]) || Text[at] == '\r' && Text[at + 1] == '\n') ? at + 2 : at + 1;
    public void Move(int direction, bool extend)
    {
        if (!extend && SelectionLength > 0) { Select(direction < 0 ? SelectionStart : SelectionStart + SelectionLength); return; }
        Select(direction < 0 ? Previous(Cursor) : Next(Cursor), extend);
    }
    public void MoveLine(int count, bool extend)
    {
        int line = LineAt(Cursor), column = Cursor - LineStarts[line];
        int target = Math.Clamp(line + count, 0, LineStarts.Length - 1);
        Select(Math.Min(LineStarts[target] + column, LineEnd(target)), extend);
    }
    public void Insert(string value)
    {
        if (ChartLoadLimits.Exceeds((long)Text.Length - SelectionLength + value.Length, 64 * 1024 * 1024))
            throw new InvalidDataException("Source text exceeds 64 MiB characters.");
        var edit = new Edit(SelectionStart, Selection, value, Cursor, Anchor);
        if (edit.Before == edit.After) return;
        undo.Add(edit); redo.Clear();
        while (undo.Count > 1 && (undo.Count > 500 || undo.Sum(e => (long)e.Before.Length + e.After.Length) > 8 * 1024 * 1024)) undo.RemoveAt(0);
        Replace(edit.Start, edit.Before.Length, edit.After);
        Cursor = Anchor = edit.Start + value.Length;
    }
    void Replace(int start, int length, string value)
    { Text = Text[..start] + value + Text[(start + length)..]; starts = null; }
    public void Delete(bool backward)
    {
        if (SelectionLength == 0) Anchor = backward ? Previous(Cursor) : Next(Cursor);
        Insert("");
    }
    public void Undo()
    {
        if (undo.Count == 0) return;
        var edit = undo[^1]; undo.RemoveAt(undo.Count - 1); redo.Add(edit);
        Replace(edit.Start, edit.After.Length, edit.Before); Cursor = edit.Cursor; Anchor = edit.Anchor;
    }
    public void Redo()
    {
        if (redo.Count == 0) return;
        var edit = redo[^1]; redo.RemoveAt(redo.Count - 1); undo.Add(edit);
        Replace(edit.Start, edit.Before.Length, edit.After); Cursor = Anchor = edit.Start + edit.After.Length;
    }
}
