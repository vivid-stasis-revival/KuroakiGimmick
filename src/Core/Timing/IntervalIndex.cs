using System.Collections;

namespace KuroakiGimmick.Core;

/// <summary>Stable start ordering and subtree end maxima. Queries allocate nothing and retain crossing intervals.</summary>
public sealed class IntervalIndex<T>
{
    public readonly record struct Entry(T Value, double Start, double End, int Order);
    readonly Entry[] entries;
    readonly double[] maxEnd, minStart;
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
        maxEnd = new double[checked(leaf * 2)];
        minStart = new double[maxEnd.Length];
        Array.Fill(maxEnd, double.NegativeInfinity); Array.Fill(minStart, double.PositiveInfinity);
        for (int i = 0; i < entries.Length; i++) { maxEnd[leaf + i] = Math.Max(entries[i].Start, entries[i].End); minStart[leaf + i] = entries[i].Start; }
        for (int i = leaf - 1; i > 0; i--) { maxEnd[i] = Math.Max(maxEnd[i * 2], maxEnd[i * 2 + 1]); minStart[i] = minStart[i * 2]; }
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
            int node = leaf + i; maxEnd[node] = Math.Max(e.Start, e.End);
            while ((node /= 2) > 0) maxEnd[node] = Math.Max(maxEnd[node * 2], maxEnd[node * 2 + 1]);
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
                if (index.maxEnd[node] < start || index.minStart[node] > end) { NextSubtree(); continue; }
                if (node < index.leaf) { node *= 2; continue; }
                int i = node - index.leaf;
                NextSubtree();
                if (i < index.entries.Length) { Current = index.entries[i]; CurrentIndex = i; return true; }
            }
            return false;
        }
        public void Dispose() { }
        public void Reset() => throw new NotSupportedException();
    }
}
