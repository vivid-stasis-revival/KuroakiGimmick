using System.Collections;

namespace KuroakiGimmick.Core;

/// <summary>Stable start ordering and subtree end maxima. Queries allocate nothing and retain crossing intervals.</summary>
public sealed class IntervalIndex<T>
{
    public readonly record struct Entry(T Value, double Start, double End, int Order);
    readonly Entry[] entries;
    readonly double[] maxEnd;
    readonly int leaf;
    Dictionary<int, int>? orderPositions;
    public IReadOnlyList<Entry> Entries => entries;
    public IntervalIndex(IEnumerable<Entry> source) : this(source.ToArray(), true) { }
    internal IntervalIndex(Entry[] source, bool owned)
    {
        entries = owned ? source : source.ToArray();
        bool sorted = true;
        for (int i = 1; i < entries.Length; i++) if (entries[i - 1].Start > entries[i].Start) { sorted = false; break; }
        if (!sorted) Array.Sort(entries, static (a, b) => { int c = a.Start.CompareTo(b.Start); return c != 0 ? c : a.Order.CompareTo(b.Order); });
        leaf = 1;
        while (leaf < entries.Length) leaf = checked(leaf * 2);
        // Leaves already live in entries. Only internal subtree maxima need storage;
        // sorted starts let an ordered query stop at its first start beyond the window.
        // This uses one leaf-sized array instead of two 2*leaf-sized arrays.
        maxEnd = new double[leaf];
        for (int i = leaf - 1; i > 0; i--) maxEnd[i] = Math.Max(EndAt(i * 2), EndAt(i * 2 + 1));
    }
    double EndAt(int node)
    {
        if (node < leaf) return maxEnd[node];
        int i = node - leaf;
        return i < entries.Length ? Math.Max(entries[i].Start, entries[i].End) : double.NegativeInfinity;
    }
    /// <summary>Editor-only incremental replacement when start ordering is unchanged. Returns false without mutation if a full rebuild is needed. Never call concurrently with queries.</summary>
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

    public Query Overlap(double start, double end) => new(this, start, end);
    public readonly struct Query(IntervalIndex<T> index, double start, double end) : IEnumerable<Entry>
    {
        public Enumerator GetEnumerator() => new(index, start, end);
        IEnumerator<Entry> IEnumerable<Entry>.GetEnumerator() => GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
    public struct Enumerator : IEnumerator<Entry>
    {
        readonly IntervalIndex<T> index;
        readonly double start, end;
        int node;
        public Entry Current { get; private set; }
        public int CurrentIndex { get; private set; }
        object IEnumerator.Current => Current;
        internal Enumerator(IntervalIndex<T> index, double start, double end) { this.index = index; this.start = start; this.end = end; node = 1; Current = default; CurrentIndex = -1; }
        void NextSubtree()
        {
            while (node > 1 && (node & 1) != 0) node /= 2;
            node = node == 1 ? 0 : node + 1;
        }
        public bool MoveNext()
        {
            while (node != 0)
            {
                if (node < index.leaf)
                {
                    if (index.maxEnd[node] < start) { NextSubtree(); continue; }
                    node *= 2; continue;
                }
                int i = node - index.leaf;
                // Traversal is in start order. No later interval can overlap once
                // this boundary is crossed, even when an earlier Hold spans it.
                if (i >= index.entries.Length || index.entries[i].Start > end) { node = 0; break; }
                NextSubtree();
                var entry = index.entries[i];
                if (Math.Max(entry.Start, entry.End) < start) continue;
                Current = entry; CurrentIndex = i; return true;
            }
            return false;
        }
        public void Dispose() { }
        public void Reset() => throw new NotSupportedException();
    }
}
