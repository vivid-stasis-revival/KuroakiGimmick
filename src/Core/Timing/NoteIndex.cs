namespace KuroakiGimmick.Core;

/// <summary>Immutable session index. Start-time ordering preserves draw order; subtree end maxima retain crossing holds.</summary>
public sealed class NoteIndex
{
    readonly Note[] notes;
    readonly double[] maxEnd;
    public NoteIndex(IReadOnlyList<Note> source)
    {
        notes = source.Where(n => n.Type is not (3 or 4 or 5)).ToArray();
        maxEnd = new double[Math.Max(1, notes.Length * 4)];
        if (notes.Length > 0) Build(1, 0, notes.Length);
    }
    double Build(int node, int lo, int hi)
    {
        if (hi - lo == 1) return maxEnd[node] = notes[lo].End;
        int mid = lo + (hi - lo) / 2;
        return maxEnd[node] = Math.Max(Build(node * 2, lo, mid), Build(node * 2 + 1, mid, hi));
    }
    public double? NearestBeat(double beat, BpmMap map)
    {
        int i = SortedSearch.LowerBound(notes, map.Time(beat), n => n.Time);
        double? left = i > 0 ? map.Beat(notes[i - 1].Time) : null;
        double? right = i < notes.Length ? map.Beat(notes[i].Time) : null;
        return left == null ? right : right == null || beat - left.Value <= right.Value - beat ? left : right;
    }
    public IEnumerable<Note> Candidates(double earliestEnd, double latestStart)
    {
        int end = SortedSearch.UpperBound(notes, latestStart, n => n.Time);
        if (end == 0) yield break;
        // One bounded traversal stack per query, not an iterator allocation at every tree node.
        var stack = new Stack<(int Node, int Lo, int Hi)>(32);
        stack.Push((1, 0, notes.Length));
        while (stack.TryPop(out var range))
        {
            var (node, lo, hi) = range;
            if (lo >= end || maxEnd[node] < earliestEnd) continue;
            if (hi - lo == 1) { yield return notes[lo]; continue; }
            int mid = lo + (hi - lo) / 2;
            if (mid < end) stack.Push((node * 2 + 1, mid, hi));
            stack.Push((node * 2, lo, mid));
        }
    }
}
