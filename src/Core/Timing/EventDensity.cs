namespace KuroakiGimmick.Core;

/// <summary>会话持有按秒排序的事件快照，改变密度桶数量时直接重新分桶，避免再次转换 BPM。</summary>
public sealed class EventDensity
{
    readonly double[] times;
    double duration = double.NaN;
    int[] bins = [];
    /// <summary>最近分桶结果中的最高事件数，下限为 1。</summary>
    public int Peak { get; private set; } = 1;
    /// <summary>将演出事件起始拍换算为秒并排序以供重复分桶。</summary>
    /// <param name="chart">提供音符、演出事件及诊断的谱面。</param>
    /// <param name="map">在歌曲秒数与拍数之间转换的 BPM 映射。</param>
    public EventDensity(Chart chart, BpmMap map)
    {
        times = new double[chart.Mods.Count];
        for (int i = 0; i < times.Length; i++) times[i] = map.Time(chart.Mods[i].Beat);
        Array.Sort(times);
    }
    /// <summary>取得等长时间区间内的事件计数，桶数和时长相同时复用缓存。</summary>
    /// <param name="count">密度桶数，最少一个。</param>
    /// <param name="length">统计的歌曲秒数。</param>
    /// <returns>覆盖该时长的只读事件计数视图。</returns>
    public ReadOnlySpan<int> Bins(int count, double length)
    {
        count = Math.Max(1, count);
        if (bins.Length == count && duration == length) return bins;
        bins = new int[count];
        duration = length;
        Peak = 1;
        if (!double.IsFinite(length) || length <= 0) return bins;
        int previous = SortedSearch.LowerBound(times, 0, static t => t);
        for (int i = 0; i < count; i++)
        {
            int next = SortedSearch.LowerBound(times, length * (i + 1) / count, static t => t);
            bins[i] = next - previous;
            previous = next;
            Peak = Math.Max(Peak, bins[i]);
        }
        return bins;
    }
}
