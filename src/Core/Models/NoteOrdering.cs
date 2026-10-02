namespace KuroakiGimmick.Core;

/// <summary>
/// 音符排序。时间升序是二分查询的前提，稳定性则是统计口径的前提：原版按二进制的读入顺序遍历音符，
/// jack / chain 的配对与同刻音符的密度权重都依赖这个次序，所以同一时刻的音符必须保留源文件里的先后。
/// <see cref="List{T}.Sort(Comparison{T})"/> 是不稳定的内省排序，同刻音符的次序会随元素个数与实现变化。
/// </summary>
public static class NoteOrdering
{
    /// <summary>按时间稳定升序排序。OrderBy 保证相等键保持原有相对次序。</summary>
    public static void StableSortByTime(this List<Note> notes)
    {
        bool sorted = true;
        for (int i = 1; i < notes.Count; i++)
            if (notes[i - 1].Time > notes[i].Time) { sorted = false; break; }
        if (sorted) return;
        var ordered = notes.OrderBy(n => n.Time).ToArray();
        notes.Clear();
        notes.AddRange(ordered);
    }
}
