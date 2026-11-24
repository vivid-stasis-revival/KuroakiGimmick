// 将显式命中与长条节拍流组合，用于任意时刻的计分及命中特效查询。

namespace KuroakiGimmick.Core;

/// <summary>自动演奏显式保存头尾判定，用等间隔序列表示长条节拍。滚动分数使用有界检查点，支持任意方向查询。</summary>
public sealed class ScoreState
{
    /// <summary>自动判定与命中特效使用的事件类别。</summary>
    public enum HitKind
    {
        /// <summary>普通音符判定。</summary>
        Note,
        /// <summary>宽轨撞块判定。</summary>
        Wide,
        /// <summary>地雷判定。</summary>
        Mine,
        /// <summary>长条中途节拍判定。</summary>
        HoldTick
    }
    /// <summary>供计分及命中特效查询的自动演奏判定。</summary>
    /// <param name="Time">命中时刻，单位为秒。</param>
    /// <param name="Lane">用于判定及绘制的轨道编号。</param>
    /// <param name="Kind">区分普通音符、撞块、地雷和长条中途判定。</param>
    /// <param name="Lead">相对于判定时刻的绘制时间差，单位为毫秒；长条节拍使用头部位置。</param>
    public readonly record struct Hit(double Time, int Lane, HitKind Kind, double Lead);
    /// <summary>自动演奏判定使用的档位。</summary>
    public const int Tier = 0;
    /// <summary>自动演奏准确分的满分值。自动演奏采用的准确率百分比。</summary>
    public const double MaxScore = 1010000, Accuracy = 100;
    const double MineWindow = .035, HoldTailGrace = 150;
    const int MaxHoldParts = 100_000, CheckpointLimit = 4096;
    readonly Hit[] hits;
    readonly Hit[]? previewHits;
    readonly Hold[]? previewHolds;
    readonly ushort[]? previewHitOrdinals, previewHoldOrdinals;
    readonly double[]? previewHoldMaxEnd;
    /// <summary>是否为密集判定或长条建立了单独的预览索引。</summary>
    public bool HasDensePreview => previewHits != null || previewHolds != null;
    readonly Hold[] holds;
    readonly Run[] runs;
    readonly double[] runMaxEnd;
    readonly long[] runCounts;
    readonly double[] holdMaxEnd;
    readonly List<Checkpoint> checkpoints = [];
    readonly double accRate, accStep;
    /// <summary>包含长条中途判定在内的总判定数。</summary>
    public long NoteCount { get; }
    /// <summary>每个判定所占的计分值。</summary>
    public double NoteValue { get; }
    /// <summary>显式保存的命中记录数，不含算术展开的长条节拍。</summary>
    public int StoredHitCount => hits.Length;
    /// <summary>通过等间隔序列表示的长条判定流数。</summary>
    public int HoldStreamCount => holds.Length;
    /// <summary>用于恢复滚动分数的缓存检查点数量。</summary>
    public int RollingCheckpointCount => checkpoints.Count;

    readonly record struct Hold(double Start, double PeriodMs, int Count, int Lane)
    {
        public double At(int i) => Start + (i + 1) * PeriodMs / 1000;
        public double End => At(Count - 1);
    }
    // Start 对应长条头部时刻，保留原版 p × perBeat / 1000 的运算顺序。
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
    ScoreState(ScoreState source)
    {
        hits = source.hits;
        holds = source.holds;
        runs = source.runs;
        previewHits = source.previewHits;
        previewHitOrdinals = source.previewHitOrdinals;
        previewHoldOrdinals = source.previewHoldOrdinals;
        previewHolds = source.previewHolds;
        previewHoldMaxEnd = source.previewHoldMaxEnd;
        runMaxEnd = source.runMaxEnd;
        runCounts = source.runCounts;
        holdMaxEnd = source.holdMaxEnd;
        accRate = source.accRate;
        accStep = source.accStep;
        NoteCount = source.NoteCount;
        NoteValue = source.NoteValue;
        checkpoints.AddRange(source.checkpoints);
    }
    internal ScoreState ForkForRender() => new(this);

    /// <summary>从音符和 BPM 变化构建自动判定与计分索引。</summary>
    /// <param name="chart">提供音符、演出事件及诊断的谱面。</param>
    /// <param name="bpm">在歌曲秒数与拍数之间转换的 BPM 映射。</param>
    public ScoreState(Chart chart, BpmMap bpm)
    {
        int hitCount = 0;
        foreach (var note in chart.Notes)
            hitCount = checked(hitCount + (note.Type == 2 ? 2 : note.Type is 0 or 1 or 6 or 7 or 8 ? 1 : 0));
        hits = new Hit[hitCount];
        int hitIndex = 0;
        void AddHit(Hit hit) => hits[hitIndex++] = hit;
        var holdStreams = new List<Hold>();
        var groups = new Dictionary<(double Start, double Period, int Count), long>();
        foreach (var n in chart.Notes)
        {
            switch (n.Type)
            {
                case 0:
                    AddHit(new(n.Time, n.Lane, HitKind.Note, 0));
                    break;
                case 1 or 8:
                    AddHit(new(n.Time, 4 + n.Lane, HitKind.Wide, 0));
                    break;
                case 6 or 7:
                    AddHit(new(n.Time + MineWindow, n.Lane + (n.Type == 7 ? 4 : 0), HitKind.Mine, 0));
                    break;
                case 2:
                    double period = 60000 / bpm.BpmAtBeat(bpm.Beat(n.Time));
                    int parts = (int)Math.Min(MaxHoldParts, Math.Floor(Math.Max(0, (n.End - n.Time) * 1000 - HoldTailGrace) / period));
                    AddHit(new(n.Time, n.Lane, HitKind.Note, 0));
                    AddHit(new(n.End, n.Lane, HitKind.Note, (n.Time - n.End) * 1000));
                    if (parts > 0)
                    {
                        holdStreams.Add(new(n.Time, period, parts, n.Lane));
                        var key = (n.Time, period, parts);
                        groups[key] = groups.GetValueOrDefault(key) + 1;
                    }
                    break;
            }
        }
        bool hitsSorted = true;
        for (int i = 1; i < hits.Length; i++)
            if (hits[i - 1].Time > hits[i].Time)
            {
                hitsSorted = false;
                break;
            }
        if (!hitsSorted) Array.Sort(hits, (a, b) => a.Time.CompareTo(b.Time));
        var hitPreview = DensePreview.Reduce(hits, static h => h.Time, static h => (h.Lane, h.Kind, h.Lead));
        previewHits = hitPreview?.Items;
        previewHitOrdinals = hitPreview?.Ordinals;
        holds = holdStreams.OrderBy(h => h.At(0)).ToArray();
        holdMaxEnd = new double[Math.Max(1, holds.Length * 4)];
        if (holds.Length > 0) BuildHolds(holds, holdMaxEnd, 1, 0, holds.Length);
        var holdPreview = DensePreview.Reduce(holds, static h => h.At(0),
            static h => (h.Start, h.PeriodMs, h.Count, h.Lane), denseOwner: previewHits != null);
        previewHolds = holdPreview?.Items;
        previewHoldOrdinals = holdPreview?.Ordinals;
        if (previewHolds != null)
        {
            previewHoldMaxEnd = new double[Math.Max(1, previewHolds.Length * 4)];
            if (previewHolds.Length > 0) BuildHolds(previewHolds, previewHoldMaxEnd, 1, 0, previewHolds.Length);
        }
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

    static double BuildHolds(Hold[] source, double[] maxEnd, int node, int lo, int hi)
    {
        if (hi - lo == 1) return maxEnd[node] = source[lo].End;
        int mid = lo + (hi - lo) / 2;
        return maxEnd[node] = Math.Max(BuildHolds(source, maxEnd, node * 2, lo, mid), BuildHolds(source, maxEnd, node * 2 + 1, mid, hi));
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
        BuildRuns(node * 2, lo, mid);
        BuildRuns(node * 2 + 1, mid, hi);
        runMaxEnd[node] = Math.Max(runMaxEnd[node * 2], runMaxEnd[node * 2 + 1]);
        runCounts[node] = runCounts[node * 2] + runCounts[node * 2 + 1];
    }
    /// <summary>统计指定秒数及之前已发生的全部判定。</summary>
    /// <param name="time">采样时刻，单位为秒。</param>
    /// <returns>截至指定秒数已发生的判定总数。</returns>
    public long Judged(double time) => SortedSearch.UpperBound(hits, time, h => h.Time) + CountAt(1, 0, runs.Length, time);
    long CountAt(int node, int lo, int hi, double time)
    {
        if (lo == hi || runs[lo].At(0) > time) return 0;
        if (runMaxEnd[node] <= time) return runCounts[node];
        if (hi - lo == 1) return runs[lo].Weight * runs[lo].Bound(time, true);
        int mid = lo + (hi - lo) / 2;
        return CountAt(node * 2, lo, mid, time) + CountAt(node * 2 + 1, mid, hi, time);
    }
    /// <summary>返回该时刻自动演奏累计的连击数。</summary>
    /// <param name="time">采样时刻，单位为秒。</param>
    /// <returns>截至指定秒数的连击数。</returns>
    public long Combo(double time) => Judged(time);
    /// <summary>返回已判定数乘以准确分步长的即时值。</summary>
    /// <param name="time">采样时刻，单位为秒。</param>
    /// <returns>尚未平滑滚动的即时准确分。</returns>
    public double CurrentScore(double time) => Judged(time) * accStep;
    /// <summary>返回该时刻累计的 EX 原始分，每个判定计 3 分。</summary>
    /// <param name="time">采样时刻，单位为秒。</param>
    /// <returns>每次命中计三分的当前累计值。</returns>
    public long GameScore(double time) => 3 * Judged(time);
    /// <summary>采样滚动显示的准确分。</summary>
    /// <param name="time">采样时刻，单位为秒。</param>
    /// <returns>当前滚动显示的准确分。</returns>
    public double AccScore(double time) => Rolling(time).Acc;
    /// <summary>采样滚动显示的 EX 分。</summary>
    /// <param name="time">采样时刻，单位为秒。</param>
    /// <returns>当前滚动显示的 EX 分。</returns>
    public double ExScore(double time) => Rolling(time).Ex;
    /// <summary>查找给定时刻及之前最近一次命中的秒数。</summary>
    /// <param name="time">采样时刻，单位为秒。</param>
    /// <returns>最近一次命中的秒数，尚无命中时为 null。</returns>
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
            Add(node * 2, lo, mid);
            Add(node * 2 + 1, mid, hi);
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

    /// <summary>两个显示分在整个区间内都低于各自当前目标，即使没有新增判定也成立，因此可以算术累计中途节拍。仅跳过已证明区间的一半以保留数值余量。</summary>
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

    /// <summary>仅生成指定生命周期窗口内的命中特效，按时间顺序枚举。</summary>
    /// <param name="from">查询起点，单位为秒。</param>
    /// <param name="to">查询终点，单位为秒。</param>
    /// <param name="includeHoldTicks">是否枚举长条中途节拍。</param>
    /// <param name="preview">是否采用预览索引。</param>
    /// <param name="layers">预览允许的重复层数。</param>
    /// <returns>按时刻排列的命中记录。</returns>
    public IEnumerable<Hit> HitsBetween(double from, double to, bool includeHoldTicks = true, bool preview = false,
        int layers = ViewerSettings.DefaultStackLayers)
    {
        layers = ViewerSettings.ValidStackLayers(layers);
        var visibleHits = preview ? previewHits ?? hits : hits;
        var hitOrdinals = preview ? previewHitOrdinals : null;
        var visibleHolds = preview ? previewHolds ?? holds : holds;
        var visibleHoldMaxEnd = preview ? previewHoldMaxEnd ?? holdMaxEnd : holdMaxEnd;
        var queue = new PriorityQueue<(int Hold, int Index), double>();
        if (includeHoldTicks) AddHolds(1, 0, visibleHolds.Length);
        int explicitIndex = SortedSearch.LowerBound(visibleHits, from, h => h.Time);
        while ((explicitIndex < visibleHits.Length && visibleHits[explicitIndex].Time <= to) || queue.Count > 0)
        {
            while (explicitIndex < visibleHits.Length && hitOrdinals != null && hitOrdinals[explicitIndex] >= layers)
                explicitIndex++;
            if ((explicitIndex >= visibleHits.Length || visibleHits[explicitIndex].Time > to) && queue.Count == 0) break;
            if (explicitIndex < visibleHits.Length && visibleHits[explicitIndex].Time <= to &&
                (!queue.TryPeek(out _, out double next) || visibleHits[explicitIndex].Time <= next))
            {
                yield return visibleHits[explicitIndex++];
                continue;
            }
            var cursor = queue.Dequeue();
            var hold = visibleHolds[cursor.Hold];
            yield return new(hold.At(cursor.Index), hold.Lane, HitKind.HoldTick, -(cursor.Index + 1) * hold.PeriodMs);
            int i = cursor.Index + 1;
            if (i < hold.Count && hold.At(i) <= to) queue.Enqueue((cursor.Hold, i), hold.At(i));
        }
        void AddHolds(int node, int lo, int hi)
        {
            if (lo == hi || visibleHoldMaxEnd[node] < from || visibleHolds[lo].At(0) > to) return;
            if (hi - lo == 1)
            {
                if (preview && previewHoldOrdinals != null && previewHoldOrdinals[lo] >= layers) return;
                var hold = visibleHolds[lo];
                int i = new Run(hold.Start, hold.PeriodMs, hold.Count, 1).Bound(from, false);
                if (i < hold.Count && hold.At(i) <= to) queue.Enqueue((lo, i), hold.At(i));
                return;
            }
            int mid = lo + (hi - lo) / 2;
            AddHolds(node * 2, lo, mid);
            AddHolds(node * 2 + 1, mid, hi);
        }
    }
}
