// 管理源码编辑器的草稿、UTF-16 光标、行索引与局部撤销。

namespace KuroakiGimmick.Core.Editing;

/// <summary>源码草稿：保留原换行，光标/选择和局部撤销不触碰工程文档。</summary>
public sealed class SourceTextBuffer
{
    sealed record Edit(int Start, string Before, string After, int Cursor, int Anchor);
    readonly List<Edit> undo = [], redo = [];
    int[]? starts;
    /// <summary>当前可编辑的完整文本。</summary>
    public string Text { get; private set; }
    /// <summary>用于脏状态比较的上次保存内容。</summary>
    public string Original { get; private set; }
    /// <summary>按初始文本检测的换行符，未匹配 CRLF 或 CR 时使用 LF。</summary>
    public string NewLine { get; }
    /// <summary>活动光标在 UTF-16 文本中的索引。</summary>
    public int Cursor { get; private set; }
    /// <summary>选区固定端的 UTF-16 索引。</summary>
    public int Anchor { get; private set; }
    /// <summary>将当前文本记为新的保存基准。</summary>
    public void MarkSaved() => Original = Text;
    /// <summary>当前文本是否与保存基准不同。</summary>
    public bool Dirty => Text != Original;
    /// <summary>光标与锚点中较小的索引。</summary>
    public int SelectionStart => Math.Min(Cursor, Anchor);
    /// <summary>以 UTF-16 代码单元计的选区长度。</summary>
    public int SelectionLength => Math.Abs(Cursor - Anchor);
    /// <summary>当前选区的文本，无选区时为空字符串。</summary>
    public string Selection => Text.Substring(SelectionStart, SelectionLength);
    /// <summary>各行起始索引，按需缓存直至文本被替换。</summary>
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
    /// <summary>将位置限制在文本内并返回所在行的零基索引。</summary>
    /// <param name="position">文本中的 UTF-16 位置。</param>
    /// <returns>该位置所在行的零基索引。</returns>
    public int LineAt(int position)
    {
        int at = Array.BinarySearch(LineStarts, Math.Clamp(position, 0, Text.Length));
        return at >= 0 ? at : ~at - 1;
    }
    /// <summary>返回指定行排除末尾 CR 和 LF 后的结束索引。</summary>
    /// <param name="line">零基行号。</param>
    /// <returns>排除换行符后的结束索引。</returns>
    public int LineEnd(int line)
    {
        int end = line + 1 < LineStarts.Length ? LineStarts[line + 1] : Text.Length;
        while (end > LineStarts[line] && Text[end - 1] is '\r' or '\n') end--;
        return end;
    }
    /// <summary>用源码初始化草稿、保存基准与换行约定。</summary>
    /// <param name="text">作为保存基准的原始文本。</param>
    public SourceTextBuffer(string text)
    {
        Text = Original = text;
        NewLine = text.Contains("\r\n") ? "\r\n" : text.Contains('\r') ? "\r" : "\n";
    }
    /// <summary>在指定位置放置光标，落在代理项对中间时退回字符起点。</summary>
    /// <param name="position">文本中的 UTF-16 位置。</param>
    /// <param name="extend">是否保留选区锚点。</param>
    public void Select(int position, bool extend = false)
    {
        Cursor = Math.Clamp(position, 0, Text.Length);
        if (Cursor > 0 && Cursor < Text.Length && char.IsLowSurrogate(Text[Cursor]) && char.IsHighSurrogate(Text[Cursor - 1])) Cursor--;
        if (!extend) Anchor = Cursor;
    }
    /// <summary>将选区扩展到整个文本。</summary>
    public void SelectAll()
    {
        Anchor = 0;
        Cursor = Text.Length;
    }
    int Previous(int at) => at <= 0 ? 0 : at >= 2 &&
        (char.IsLowSurrogate(Text[at - 1]) && char.IsHighSurrogate(Text[at - 2]) || Text[at - 1] == '\n' && Text[at - 2] == '\r') ? at - 2 : at - 1;
    int Next(int at) => at >= Text.Length ? Text.Length : at + 1 < Text.Length &&
        (char.IsHighSurrogate(Text[at]) && char.IsLowSurrogate(Text[at + 1]) || Text[at] == '\r' && Text[at + 1] == '\n') ? at + 2 : at + 1;
    /// <summary>向前或向后移动字符；未扩展选区时优先收拢已有选区。</summary>
    /// <param name="direction">负值向前，其余向后。</param>
    /// <param name="extend">是否扩展选区。</param>
    public void Move(int direction, bool extend)
    {
        if (!extend && SelectionLength > 0)
        {
            Select(direction < 0 ? SelectionStart : SelectionStart + SelectionLength);
            return;
        }
        Select(direction < 0 ? Previous(Cursor) : Next(Cursor), extend);
    }
    /// <summary>按行移动光标并尽量保留列，超出短行时落在行尾。</summary>
    /// <param name="count">要纵向跨过的行数。</param>
    /// <param name="extend">是否扩展选区。</param>
    public void MoveLine(int count, bool extend)
    {
        int line = LineAt(Cursor), column = Cursor - LineStarts[line];
        int target = Math.Clamp(line + count, 0, LineStarts.Length - 1);
        Select(Math.Min(LineStarts[target] + column, LineEnd(target)), extend);
    }
    /// <summary>用新文本替换选区，并为有效更改记录局部撤销项。</summary>
    /// <param name="value">替换当前选区的文本。</param>
    /// <exception cref="InvalidDataException">替换后的源码长度超过当前策略允许的字符数。</exception>
    public void Insert(string value)
    {
        if (ChartLoadLimits.Exceeds((long)Text.Length - SelectionLength + value.Length, 64 * 1024 * 1024))
            throw new InvalidDataException("Source text exceeds 64 MiB characters.");
        var edit = new Edit(SelectionStart, Selection, value, Cursor, Anchor);
        if (edit.Before == edit.After) return;
        undo.Add(edit);
        redo.Clear();
        while (undo.Count > 1 && (undo.Count > 500 || undo.Sum(e => (long)e.Before.Length + e.After.Length) > 8 * 1024 * 1024)) undo.RemoveAt(0);
        Replace(edit.Start, edit.Before.Length, edit.After);
        Cursor = Anchor = edit.Start + value.Length;
    }
    void Replace(int start, int length, string value)
    {
        Text = Text[..start] + value + Text[(start + length)..];
        starts = null;
    }
    /// <summary>删除选区，无选区时根据方向选取相邻字符或换行再删除。</summary>
    /// <param name="backward">无选区时是否向前删除。</param>
    public void Delete(bool backward)
    {
        if (SelectionLength == 0) Anchor = backward ? Previous(Cursor) : Next(Cursor);
        Insert("");
    }
    /// <summary>撤销最近的局部替换并恢复原光标与锚点。</summary>
    public void Undo()
    {
        if (undo.Count == 0) return;
        var edit = undo[^1];
        undo.RemoveAt(undo.Count - 1);
        redo.Add(edit);
        Replace(edit.Start, edit.After.Length, edit.Before);
        Cursor = edit.Cursor;
        Anchor = edit.Anchor;
    }
    /// <summary>重新应用刚撤销的替换，将光标折叠至插入内容末尾。</summary>
    public void Redo()
    {
        if (redo.Count == 0) return;
        var edit = redo[^1];
        redo.RemoveAt(redo.Count - 1);
        undo.Add(edit);
        Replace(edit.Start, edit.Before.Length, edit.After);
        Cursor = Anchor = edit.Start + edit.After.Length;
    }
}
