namespace KuroakiGimmick.Core;

/// <summary>Session-owned immutable density snapshot; bin resizing uses sorted event times rather than BPM evaluation.</summary>
public sealed class EventDensity
{
    readonly double[] times;
    double duration = double.NaN;
    int[] bins = [];
    public int Peak { get; private set; } = 1;
    public EventDensity(Chart chart, BpmMap map)
    {
        times = new double[chart.Mods.Count];
        for (int i = 0; i < times.Length; i++) times[i] = map.Time(chart.Mods[i].Beat);
        Array.Sort(times);
    }
    public ReadOnlySpan<int> Bins(int count, double length)
    {
        count = Math.Max(1, count);
        if (bins.Length == count && duration == length) return bins;
        bins = new int[count]; duration = length; Peak = 1;
        if (!double.IsFinite(length) || length <= 0) return bins;
        int previous = SortedSearch.LowerBound(times, 0, static t => t);
        for (int i = 0; i < count; i++)
        {
            int next = SortedSearch.LowerBound(times, length * (i + 1) / count, static t => t);
            bins[i] = next - previous; previous = next; Peak = Math.Max(Peak, bins[i]);
        }
        return bins;
    }
}
