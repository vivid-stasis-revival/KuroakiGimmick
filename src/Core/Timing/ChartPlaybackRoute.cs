namespace KuroakiGimmick.Core;

/// <summary>一次性的音乐控制回调展开成有限的播放路线，包含向前和向后的 seek。</summary>
public sealed class ChartPlaybackRoute
{
    /// <summary>一段等速播放：Start/End 是谱面秒，PlaybackStart 是这段在播放时间轴上的起点秒。</summary>
    public sealed record Leg(double Start, double End, double PlaybackStart, double Rate)
    { public double Duration => (End - Start) / Rate; }
    readonly List<Leg> legs = [];
    public IReadOnlyList<Leg> Legs => legs;
    public double Duration => legs.Count == 0 ? 0 : legs[^1].PlaybackStart + legs[^1].Duration;
    readonly double end;
    public ChartPlaybackRoute(ChartPlaybackMap map, double start, double end)
    {
        this.end = end;
        double cursor = start, rate = start <= 0 ? 1 : map.RateAt(start), wall = 0;
        var controls = map.Controls.Where(c => start <= 0 || c.Time > start || c.Time == start && c.Target.HasValue).ToArray();
        int index = 0;
        while (cursor < end)
        {
            double dispatch = cursor;
            while (index < controls.Length && controls[index].Time <= Math.Max(dispatch, cursor))
            {
                var c = controls[index++];
                if (c.Target is double target) cursor = Math.Max(0, target);
                else if (rate * c.Multiplier is >= .01 and <= 100) rate *= c.Multiplier;
            }
            if (cursor >= end) break;
            double next = index < controls.Length ? Math.Min(end, controls[index].Time) : end;
            if (next <= cursor) continue;
            legs.Add(new(cursor, next, wall, rate)); wall += (next - cursor) / rate;
            // 互相跳转的控制可以构成无限循环，用段数和总时长两个上限强制终止，而不是让加载卡死。
            if (legs.Count > 10000 || wall > 86400) throw new InvalidDataException("Music-control route exceeds 10000 segments or 24 hours.");
            cursor = next;
        }
    }
    /// <summary>二分定位播放秒所在的 leg；依赖 legs 按 PlaybackStart 升序，这由构造顺序保证。</summary>
    public int IndexAt(double playback)
    {
        int lo = 0, hi = legs.Count;
        while (lo < hi) { int mid = (lo + hi) / 2; if (legs[mid].PlaybackStart <= playback) lo = mid + 1; else hi = mid; }
        return Math.Max(0, lo - 1);
    }
    /// <summary>播放秒 → 谱面秒。走完整条路线后固定停在 end，不再外推。</summary>
    public double ChartAt(double playback)
    {
        if (legs.Count == 0 || playback >= Duration) return end;
        var leg = legs[IndexAt(playback)]; return leg.Start + Math.Max(0, playback - leg.PlaybackStart) * leg.Rate;
    }
    public double RateAt(double playback) => legs.Count == 0 ? 1 : legs[IndexAt(playback)].Rate;
}
