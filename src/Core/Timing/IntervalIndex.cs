// 通过有序起点和子树最大终点索引跨越查询区间的事件。

using System.Collections;

namespace KuroakiGimmick.Core;

/// <summary>按区间起点排列记录，并缓存子树的最大终点；查询保留跨过窗口起点的区间。</summary>
/// <typeparam name="T">与每个区间关联的业务值类型。</typeparam>
public sealed class IntervalIndex<T>
{
    /// <summary>具有稳定来源顺序的区间索引项。</summary>
    /// <param name="Value">区间关联的数据。</param>
    /// <param name="Start">区间起点。</param>
    /// <param name="End">区间终点。</param>
    /// <param name="Order">用于同起点排序和增量更新的来源编号。</param>
    public readonly record struct Entry(T Value, double Start, double End, int Order);
    readonly Entry[] entries;
    readonly double[] maxEnd;
    readonly int leaf;
    Dictionary<int, int>? orderPositions;
    /// <summary>按起点顺序存储的区间记录。</summary>
    public IReadOnlyList<Entry> Entries => entries;
    /// <summary>复制输入记录并建立子树最大结束位置的索引。</summary>
    /// <param name="source">要复制并建立索引的区间记录。</param>
    public IntervalIndex(IEnumerable<Entry> source) : this(source.ToArray(), true)
    {
    }
    internal IntervalIndex(Entry[] source, bool owned)
    {
        entries = owned ? source : source.ToArray();
        bool sorted = true;
        for (int i = 1; i < entries.Length; i++) if (entries[i - 1].Start > entries[i].Start)
        {
            sorted = false;
            break;
        }
        if (!sorted) Array.Sort(entries, static (a, b) =>
{
    int c = a.Start.CompareTo(b.Start);
    return c != 0 ? c : a.Order.CompareTo(b.Order);
});
        leaf = 1;
        while (leaf < entries.Length) leaf = checked(leaf * 2);
        // 叶节点已经存放在 entries，只需为内部节点保存最大终点。起点有序，超过查询窗口后即可停止；额外数组只需 leaf 项。
        maxEnd = new double[leaf];
        for (int i = leaf - 1; i > 0; i--) maxEnd[i] = Math.Max(EndAt(i * 2), EndAt(i * 2 + 1));
    }
    double EndAt(int node)
    {
        if (node < leaf) return maxEnd[node];
        int i = node - leaf;
        return i < entries.Length ? Math.Max(entries[i].Start, entries[i].End) : double.NegativeInfinity;
    }
    /// <summary>仅供编辑器在起点顺序不变时增量替换。若必须重建则返回 false，原区间不变。更新与查询不能并发执行。</summary>
    /// <param name="replacement">包含更新内容、保留来源编号与起点的区间集合。</param>
    /// <returns>可直接增量更新时为 true，需重建时为 false。</returns>
    public bool TryUpdate(IReadOnlyList<Entry> replacement)
    {
        if (replacement.Count != entries.Length) return false;
        orderPositions ??= entries.Select((e, i) => (e.Order, Index: i)).ToDictionary(e => e.Order, e => e.Index);
        foreach (var e in replacement)
            if (!orderPositions.TryGetValue(e.Order, out int i) || entries[i].Start != e.Start) return false;
        foreach (var e in replacement)
        {
            int i = orderPositions[e.Order];
            if (EqualityComparer<Entry>.Default.Equals(entries[i], e)) continue;
            entries[i] = e;
            int node = leaf + i;
            while ((node /= 2) > 0) maxEnd[node] = Math.Max(EndAt(node * 2), EndAt(node * 2 + 1));
        }
        return true;
    }

    /// <summary>返回与闭区间重叠的记录视图。</summary>
    /// <param name="start">查询下界，含端点。</param>
    /// <param name="end">查询上界，含端点。</param>
    /// <returns>按起点顺序枚举重叠项的视图。</returns>
    public Query Overlap(double start, double end) => new(this, start, end);
    /// <summary>按需遍历重叠项的值类型查询视图。</summary>
    /// <param name="index">被查询的区间索引。</param>
    /// <param name="start">查询下界。</param>
    /// <param name="end">查询上界。</param>
    public readonly struct Query(IntervalIndex<T> index, double start, double end) : IEnumerable<Entry>
    {
        /// <summary>返回遍历当前区间查询的值类型枚举器。</summary>
        /// <returns>遍历重叠区间的枚举器。</returns>
        public Enumerator GetEnumerator() => new(index, start, end);
        IEnumerator<Entry> IEnumerable<Entry>.GetEnumerator() => GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
    /// <summary>按起点顺序遍历并跳过不可能重叠的子树。</summary>
    public struct Enumerator : IEnumerator<Entry>
    {
        readonly IntervalIndex<T> index;
        readonly double start, end;
        int node;
        /// <inheritdoc/>
        public Entry Current { get; private set; }
        /// <summary>最近命中项在底层有序数组中的索引。</summary>
        public int CurrentIndex { get; private set; }
        object IEnumerator.Current => Current;
        internal Enumerator(IntervalIndex<T> index, double start, double end)
        {
            this.index = index;
            this.start = start;
            this.end = end;
            node = 1;
            Current = default;
            CurrentIndex = -1;
        }
        void NextSubtree()
        {
            while (node > 1 && (node & 1) != 0) node /= 2;
            node = node == 1 ? 0 : node + 1;
        }
        /// <inheritdoc/>
        public bool MoveNext()
        {
            while (node != 0)
            {
                if (node < index.leaf)
                {
                    if (index.maxEnd[node] < start)
                    {
                        NextSubtree();
                        continue;
                    }
                    node *= 2;
                    continue;
                }
                int i = node - index.leaf;
                // 按起点顺序遍历，越过窗口后其余区间均不可能重叠，即使较早的长条跨过此边界也不受影响。
                if (i >= index.entries.Length || index.entries[i].Start > end)
                {
                    node = 0;
                    break;
                }
                NextSubtree();
                var entry = index.entries[i];
                if (Math.Max(entry.Start, entry.End) < start) continue;
                Current = entry;
                CurrentIndex = i;
                return true;
            }
            return false;
        }
        /// <inheritdoc/>
        public void Dispose()
        {
        }
        /// <inheritdoc/>
        /// <exception cref="NotSupportedException">枚举器不支持复位。</exception>
        public void Reset() => throw new NotSupportedException();
    }
}
