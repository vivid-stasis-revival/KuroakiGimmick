using System.Collections;
using System.Runtime.InteropServices;

namespace KuroakiGimmick.Core;

[StructLayout(LayoutKind.Sequential)]
public readonly struct NoteRuntimeData(double time, double end, uint lane)
{
    public readonly double Time = time, End = end;
    public readonly uint Lane = lane, Reserved = 0;
}

/// <summary>Immutable session snapshot. Beat conversion and native note payload are built once, never per frame.</summary>
public sealed class NoteIndex
{
    public readonly record struct Item(Note Note, double Beat, double EndBeat);
    readonly IntervalIndex<Item> index;
    readonly IntervalIndex<Item>? previewIndex;
    readonly ushort[]? previewOrdinals;
    internal NoteRuntimeData[] Runtime { get; }
    internal NoteRuntimeData[]? PreviewRuntime { get; }
    public bool HasDensePreview => previewIndex != null;
    public int Count => index.Entries.Count;
    public NoteIndex(IReadOnlyList<Note> source, BpmMap? map = null)
    {
        int count = 0;
        for (int i = 0; i < source.Count; i++) if (source[i].Type is not (3 or 4 or 5)) count++;
        var items = new IntervalIndex<Item>.Entry[count];
        int next = 0;
        for (int i = 0; i < source.Count; i++)
        {
            var n = source[i];
            if (n.Type is 3 or 4 or 5) continue;
            items[next++] = new(new(n, map?.Beat(n.Time) ?? double.NaN, map?.Beat(Math.Max(n.Time, n.End)) ?? double.NaN), n.Time, n.End, i);
        }
        index = new(items, owned: true);
        var preview = DensePreview.Reduce(index.Entries, static e => e.Start,
            static e => (e.Value.Note.Type, e.Value.Note.Lane, e.End));
        if (preview != null)
        {
            previewIndex = new(preview.Items, owned: true);
            previewOrdinals = preview.Ordinals;
            PreviewRuntime = new NoteRuntimeData[preview.Items.Length];
            for (int i = 0; i < preview.Items.Length; i++)
            {
                var n = preview.Items[i].Value.Note;
                PreviewRuntime[i] = new(n.Time, n.End, unchecked((uint)(n.Type is 1 or 7 or 8 ? n.Lane + 4 : n.Lane)));
            }
        }
        Runtime = new NoteRuntimeData[count];
        for (int i = 0; i < Runtime.Length; i++)
        {
            var n = index.Entries[i].Value.Note;
            Runtime[i] = new(n.Time, n.End, unchecked((uint)(n.Type is 1 or 7 or 8 ? n.Lane + 4 : n.Lane)));
        }
    }
    public double? NearestBeat(double beat, BpmMap map)
    {
        int i = SortedSearch.LowerBound(index.Entries, map.Time(beat), static n => n.Start);
        double? left = i > 0 ? map.Beat(index.Entries[i - 1].Start) : null;
        double? right = i < Count ? map.Beat(index.Entries[i].Start) : null;
        return left == null ? right : right == null || beat - left.Value <= right.Value - beat ? left : right;
    }
    public IntervalIndex<Item>.Query Visible(double earliestEnd, double latestStart) => index.Overlap(earliestEnd, latestStart);
    public PreviewQuery PreviewVisible(double earliestEnd, double latestStart,
        int layers = ViewerSettings.DefaultStackLayers, bool enabled = true)
        => new((enabled ? previewIndex ?? index : index).Overlap(earliestEnd, latestStart),
            enabled ? previewOrdinals : null, ViewerSettings.ValidStackLayers(layers));
    public readonly struct PreviewQuery(IntervalIndex<Item>.Query query, ushort[]? ordinals, int layers)
        : IEnumerable<IntervalIndex<Item>.Entry>
    {
        public PreviewEnumerator GetEnumerator() => new(query.GetEnumerator(), ordinals, layers);
        IEnumerator<IntervalIndex<Item>.Entry> IEnumerable<IntervalIndex<Item>.Entry>.GetEnumerator() => GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
    public struct PreviewEnumerator(IntervalIndex<Item>.Enumerator source, ushort[]? ordinals, int layers)
        : IEnumerator<IntervalIndex<Item>.Entry>
    {
        public IntervalIndex<Item>.Entry Current => source.Current;
        public int CurrentIndex => source.CurrentIndex;
        object IEnumerator.Current => Current;
        public bool MoveNext()
        {
            while (source.MoveNext())
                if (ordinals == null || ordinals[source.CurrentIndex] < layers) return true;
            return false;
        }
        public void Dispose() { }
        public void Reset() => throw new NotSupportedException();
    }
    public Query Candidates(double earliestEnd, double latestStart) => new(Visible(earliestEnd, latestStart));
    public readonly struct Query(IntervalIndex<Item>.Query query) : IEnumerable<Note>
    {
        public Enumerator GetEnumerator() => new(query.GetEnumerator());
        IEnumerator<Note> IEnumerable<Note>.GetEnumerator() => GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
    public struct Enumerator(IntervalIndex<Item>.Enumerator source) : IEnumerator<Note>
    {
        public Note Current => source.Current.Value.Note;
        object IEnumerator.Current => Current;
        public bool MoveNext() => source.MoveNext();
        public void Dispose() { }
        public void Reset() => throw new NotSupportedException();
    }
}
