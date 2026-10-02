namespace KuroakiGimmick.Core;

/// <summary>
/// Autoplay judgements: explicit heads/tails and compact arithmetic hold streams.
/// Rolling scores use bounded checkpoints, not one pair of doubles per future tick.
/// All queries remain independent of playback direction.
/// </summary>
public sealed class ScoreState
{
    public enum HitKind { Note, Wide, Mine, HoldTick }
    public readonly record struct Hit(double Time, int Lane, HitKind Kind, double Lead);
    public const int Tier = 0;
    public const double MaxScore = 1010000, Accuracy = 100;
    const double MineWindow = .035, HoldTailGrace = 150;
    const int MaxHoldParts = 100_000, CheckpointLimit = 4096;
    readonly Hit[] hits;
    readonly Hold[] holds;
    readonly Run[] runs;
    readonly double[] runMaxEnd;
    readonly long[] runCounts;
    readonly double[] holdMaxEnd;
    readonly List<Checkpoint> checkpoints = [];
    readonly double accRate, accStep;
    public long NoteCount { get; }
    public double NoteValue { get; }
    public int StoredHitCount => hits.Length;
    public int HoldStreamCount => holds.Length;
    public int RollingCheckpointCount => checkpoints.Count;

    readonly record struct Hold(double Start, double PeriodMs, int Count, int Lane)
    {
        public double At(int i) => Start + (i + 1) * PeriodMs / 1000;
        public double End => At(Count - 1);
    }
    // Start is the source hold head, preserving the original p * perBeat / 1000 arithmetic.
    readonly record struct Run(double Start, double PeriodMs, int Count, long Weight)
    {
        public double At(int i) => PeriodMs == 0 ? Start : Start + (i + 1) * PeriodMs / 1000;
        public double End => At(Count - 1);
        public int Bound(double time, bool inclusive)
        {
            int lo = 0, hi = Count;
            while (lo < hi)
            {
                int mid = lo + (hi - lo) / 2;
                if (inclusive ? At(mid) <= time : At(mid) < time) lo = mid + 1;
                else hi = mid;
            }
            return lo;
        }
    }
    readonly record struct Checkpoint(double Time, long Count, double Acc, double Ex);
    readonly record struct Cursor(int Run, int Index);

    public ScoreState(Chart chart, BpmMap bpm)
    {
        var explicitHits = new List<Hit>();
        var holdStreams = new List<Hold>();
        var groups = new Dictionary<(double Start, double Period, int Count), long>();
        foreach (var n in chart.Notes)
        {
            switch (n.Type)
            {
                case 0: explicitHits.Add(new(n.Time, n.Lane, HitKind.Note, 0)); break;
                case 1 or 8: explicitHits.Add(new(n.Time, 4 + n.Lane, HitKind.Wide, 0)); break;
                case 6 or 7: explicitHits.Add(new(n.Time + MineWindow, n.Lane + (n.Type == 7 ? 4 : 0), HitKind.Mine, 0)); break;
                case 2:
                    double period = 60000 / bpm.BpmAtBeat(bpm.Beat(n.Time));
                    int parts = (int)Math.Min(MaxHoldParts, Math.Floor(Math.Max(0, (n.End - n.Time) * 1000 - HoldTailGrace) / period));
                    explicitHits.Add(new(n.Time, n.Lane, HitKind.Note, 0));
                    explicitHits.Add(new(n.End, n.Lane, HitKind.Note, (n.Time - n.End) * 1000));
                    if (parts > 0)
                    {
                        holdStreams.Add(new(n.Time, period, parts, n.Lane));
                        var key = (n.Time, period, parts);
                        groups[key] = groups.GetValueOrDefault(key) + 1;
                    }
                    break;
            }
        }
        explicitHits.Sort((a, b) => a.Time.CompareTo(b.Time));
        hits = explicitHits.ToArray();
        holds = holdStreams.OrderBy(h => h.At(0)).ToArray();
        holdMaxEnd = new double[Math.Max(1, holds.Length * 4)];
        if (holds.Length > 0) BuildHolds(1, 0, holds.Length);
        runs = groups.Select(g => new Run(g.Key.Start, g.Key.Period, g.Key.Count, g.Value)).OrderBy(r => r.At(0)).ToArray();
        runMaxEnd = new double[Math.Max(1, runs.Length * 4)];
        runCounts = new long[runMaxEnd.Length];
        if (runs.Length > 0) BuildRuns(1, 0, runs.Length);
        NoteCount = hits.LongLength + (runs.Length == 0 ? 0 : runCounts[1]);
        NoteValue = NoteCount == 0 ? 0 : 1000000.0 / NoteCount;
        accStep = 1.01 * NoteValue;
        accRate = NoteCount == 0 ? 0 : 60 * 359800.0 / NoteCount;
        BuildCheckpoints();
    }

    double BuildHolds(int node, int lo, int hi)
    {
        if (hi - lo == 1) return holdMaxEnd[node] = holds[lo].End;
        int mid = lo + (hi - lo) / 2;
        return holdMaxEnd[node] = Math.Max(BuildHolds(node * 2, lo, mid), BuildHolds(node * 2 + 1, mid, hi));
    }
    void BuildRuns(int node, int lo, int hi)
    {
        if (hi - lo == 1)
        {
            runMaxEnd[node] = runs[lo].End;
            runCounts[node] = runs[lo].Weight * runs[lo].Count;
            return;
        }
        int mid = lo + (hi - lo) / 2;
        BuildRuns(node * 2, lo, mid); BuildRuns(node * 2 + 1, mid, hi);
        runMaxEnd[node] = Math.Max(runMaxEnd[node * 2], runMaxEnd[node * 2 + 1]);
        runCounts[node] = runCounts[node * 2] + runCounts[node * 2 + 1];
    }
    public long Judged(double time) => SortedSearch.UpperBound(hits, time, h => h.Time) + CountAt(1, 0, runs.Length, time);
    long CountAt(int node, int lo, int hi, double time)
    {
        if (lo == hi || runs[lo].At(0) > time) return 0;
        if (runMaxEnd[node] <= time) return runCounts[node];
        if (hi - lo == 1) return runs[lo].Weight * runs[lo].Bound(time, true);
        int mid = lo + (hi - lo) / 2;
        return CountAt(node * 2, lo, mid, time) + CountAt(node * 2 + 1, mid, hi, time);
    }
    public long Combo(double time) => Judged(time);
    public double CurrentScore(double time) => Judged(time) * accStep;
    public long GameScore(double time) => 3 * Judged(time);
    public double AccScore(double time) => Rolling(time).Acc;
    public double ExScore(double time) => Rolling(time).Ex;
    public double? LastHit(double time)
    {
        int i = SortedSearch.UpperBound(hits, time, h => h.Time);
        double last = Math.Max(i == 0 ? double.NegativeInfinity : hits[i - 1].Time, LastAt(1, 0, runs.Length, time));
        return double.IsNegativeInfinity(last) ? null : last;
    }
    double LastAt(int node, int lo, int hi, double time)
    {
        if (lo == hi || runs[lo].At(0) > time) return double.NegativeInfinity;
        if (runMaxEnd[node] <= time) return runMaxEnd[node];
        if (hi - lo == 1)
        {
            int count = runs[lo].Bound(time, true);
            return count == 0 ? double.NegativeInfinity : runs[lo].At(count - 1);
        }
        int mid = lo + (hi - lo) / 2;
        return Math.Max(LastAt(node * 2, lo, mid, time), LastAt(node * 2 + 1, mid, hi, time));
    }

    PriorityQueue<Cursor, double> Queue(double after, double until)
    {
        var queue = new PriorityQueue<Cursor, double>();
        int explicitIndex = SortedSearch.UpperBound(hits, after, h => h.Time);
        if (explicitIndex < hits.Length && hits[explicitIndex].Time <= until)
            queue.Enqueue(new(-1, explicitIndex), hits[explicitIndex].Time);
        Add(1, 0, runs.Length);
        return queue;
        void Add(int node, int lo, int hi)
        {
            if (lo == hi || runMaxEnd[node] <= after || runs[lo].At(0) > until) return;
            if (hi - lo == 1)
            {
                int i = runs[lo].Bound(after, true);
                if (i < runs[lo].Count && runs[lo].At(i) <= until) queue.Enqueue(new(lo, i), runs[lo].At(i));
                return;
            }
            int mid = lo + (hi - lo) / 2;
            Add(node * 2, lo, mid); Add(node * 2 + 1, mid, hi);
        }
    }
    Checkpoint Advance(Checkpoint state, Run run, int first, int count)
    {
        double start = run.At(first), end = run.At(first + count - 1);
        double acc = state.Count == 0 ? 0 : Math.Min(state.Count * accStep, state.Acc + accRate * (start - state.Time));
        double ex = state.Count == 0 ? 0 : Math.Min(state.Count * 3.0, state.Ex + 60 * (start - state.Time));
        long beforeLast = state.Count + (count - 1L) * run.Weight;
        return new(end, state.Count + count * run.Weight,
            Math.Min(beforeLast * accStep, acc + accRate * (end - start)),
            Math.Min(beforeLast * 3.0, ex + 60 * (end - start)));
    }
    void BuildCheckpoints()
    {
        long stride = Math.Max(256, (NoteCount + CheckpointLimit - 1) / CheckpointLimit), next = stride;
        var queue = Queue(double.NegativeInfinity, double.PositiveInfinity);
        var state = new Checkpoint(double.NegativeInfinity, 0, 0, 0);
        double last = Math.Max(hits.Length == 0 ? double.NegativeInfinity : hits[^1].Time,
            runs.Length == 0 ? double.NegativeInfinity : runMaxEnd[1]);
        while (queue.TryPeek(out _, out double upcoming))
        {
            if (SkipBusy(ref state, last, upcoming))
            {
                queue = Queue(state.Time, double.PositiveInfinity);
                SaveCheckpoint();
                continue;
            }
            var cursor = queue.Dequeue();
            if (cursor.Run == -1)
            {
                state = AdvanceExplicit(state, cursor.Index, queue, double.PositiveInfinity);
                SaveCheckpoint();
                continue;
            }
            var run = runs[cursor.Run];
            int end = queue.TryPeek(out _, out double other) ? Math.Max(cursor.Index + 1, run.Bound(other, false)) : run.Count;
            int take = (int)Math.Min(end - cursor.Index, Math.Max(1, (next - state.Count + run.Weight - 1) / run.Weight));
            state = Advance(state, run, cursor.Index, take);
            SaveCheckpoint();
            int following = cursor.Index + take;
            if (following < run.Count) queue.Enqueue(new(cursor.Run, following), run.At(following));
        }
        if (state.Count > 0 && (checkpoints.Count == 0 || checkpoints[^1].Time != state.Time)) checkpoints.Add(state);
        void SaveCheckpoint()
        {
            if (checkpoints.Count > 0 && checkpoints[^1].Time == state.Time) checkpoints[^1] = state;
            if (state.Count < next) return;
            if (checkpoints.Count == 0 || checkpoints[^1].Time != state.Time) checkpoints.Add(state);
            next = state.Count + stride;
        }
    }
    Checkpoint AdvanceExplicit(Checkpoint state, int index, PriorityQueue<Cursor, double> queue, double until)
    {
        double time = hits[index].Time;
        int end = SortedSearch.UpperBound(hits, time, h => h.Time);
        state = Advance(state, new Run(time, 0, 1, end - index), 0, 1);
        if (end < hits.Length && hits[end].Time <= until) queue.Enqueue(new(-1, end), hits[end].Time);
        return state;
    }

    /// <summary>
    /// Both displays are still below their *current* targets throughout this interval, even if no new hits arrive.
    /// Therefore every intervening tick can be counted arithmetically without visiting it. Half the proven interval
    /// leaves a numerical margin and makes dense, overlapping long holds cheap without approximating the scores.
    /// </summary>
    bool SkipBusy(ref Checkpoint state, double until, double upcoming)
    {
        if (state.Count == 0 || accRate == 0) return false;
        double seconds = Math.Min((state.Count * accStep - state.Acc) / accRate, (state.Count * 3.0 - state.Ex) / 60);
        double at = Math.Min(until, state.Time + seconds * .5);
        if (!(at > upcoming + .1)) return false;
        double elapsed = at - state.Time;
        state = new(at, Judged(at), state.Acc + accRate * elapsed, state.Ex + 60 * elapsed);
        return true;
    }

    double cachedTime = double.NaN;
    (double Acc, double Ex) cachedRolling;
    (double Acc, double Ex) Rolling(double time)
    {
        if (time == cachedTime) return cachedRolling;
        int i = SortedSearch.UpperBound(checkpoints, time, c => c.Time) - 1;
        var state = i >= 0 ? checkpoints[i] : new Checkpoint(double.NegativeInfinity, 0, 0, 0);
        var queue = Queue(state.Time, time);
        while (queue.TryPeek(out _, out double upcoming))
        {
            if (SkipBusy(ref state, time, upcoming))
            {
                queue = Queue(state.Time, time);
                continue;
            }
            var cursor = queue.Dequeue();
            if (cursor.Run == -1)
            {
                state = AdvanceExplicit(state, cursor.Index, queue, time);
                continue;
            }
            var run = runs[cursor.Run];
            int end = run.Bound(time, true);
            if (queue.TryPeek(out _, out double other)) end = Math.Min(end, Math.Max(cursor.Index + 1, run.Bound(other, false)));
            state = Advance(state, run, cursor.Index, end - cursor.Index);
            if (end < run.Count && run.At(end) <= time) queue.Enqueue(new(cursor.Run, end), run.At(end));
        }
        cachedTime = time;
        return cachedRolling = state.Count == 0 ? (0, 0) :
            (Math.Min(state.Count * accStep, state.Acc + accRate * (time - state.Time)),
             Math.Min(state.Count * 3.0, state.Ex + 60 * (time - state.Time)));
    }

    /// <summary>Only materialize effects in the requested lifetime window. Returned hits are chronological.</summary>
    public IEnumerable<Hit> HitsBetween(double from, double to, bool includeHoldTicks = true)
    {
        var queue = new PriorityQueue<(int Hold, int Index), double>();
        if (includeHoldTicks) AddHolds(1, 0, holds.Length);
        int explicitIndex = SortedSearch.LowerBound(hits, from, h => h.Time);
        while ((explicitIndex < hits.Length && hits[explicitIndex].Time <= to) || queue.Count > 0)
        {
            if (explicitIndex < hits.Length && hits[explicitIndex].Time <= to &&
                (!queue.TryPeek(out _, out double next) || hits[explicitIndex].Time <= next))
            { yield return hits[explicitIndex++]; continue; }
            var cursor = queue.Dequeue();
            var hold = holds[cursor.Hold];
            yield return new(hold.At(cursor.Index), hold.Lane, HitKind.HoldTick, -(cursor.Index + 1) * hold.PeriodMs);
            int i = cursor.Index + 1;
            if (i < hold.Count && hold.At(i) <= to) queue.Enqueue((cursor.Hold, i), hold.At(i));
        }
        void AddHolds(int node, int lo, int hi)
        {
            if (lo == hi || holdMaxEnd[node] < from || holds[lo].At(0) > to) return;
            if (hi - lo == 1)
            {
                var hold = holds[lo];
                int i = new Run(hold.Start, hold.PeriodMs, hold.Count, 1).Bound(from, false);
                if (i < hold.Count && hold.At(i) <= to) queue.Enqueue((lo, i), hold.At(i));
                return;
            }
            int mid = lo + (hi - lo) / 2;
            AddHolds(node * 2, lo, mid); AddHolds(node * 2 + 1, mid, hi);
        }
    }
}
