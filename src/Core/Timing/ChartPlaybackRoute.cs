namespace KuroakiGimmick.Core;

/// <summary>一次性的音乐控制回调展开成有限的播放路线，包含向前和向后的 seek。</summary>
public sealed class ChartPlaybackRoute
{
    /// <summary>一段等速播放：Start/End 是谱面秒，PlaybackStart 是这段在播放时间轴上的起点秒。</summary>
    /// <param name="Start">谱面起始秒数。</param>
    /// <param name="End">谱面结束秒数。</param>
    /// <param name="PlaybackStart">播放时钟上的起始秒数。</param>
    /// <param name="Rate">本段速度倍率。</param>
    public sealed record Leg(double Start, double End, double PlaybackStart, double Rate)
    {
        /// <summary>以本段倍率走完谱面区间所需的播放秒数。</summary>
        public double Duration => (End - Start) / Rate;
    }
    readonly List<Leg> legs = [];
    /// <summary>依播放顺序排列的连续谱面区间。</summary>
    public IReadOnlyList<Leg> Legs => legs;
    /// <summary>展开路线后的总播放秒数。</summary>
    public double Duration => legs.Count == 0 ? 0 : legs[^1].PlaybackStart + legs[^1].Duration;
    readonly double end;
    private ChartPlaybackRoute(double start, double end)
    {
        this.end = end;
        if (end > start) legs.Add(new(start, end, 0, 1));
    }
    /// <summary>建立从起点到终点的单倍率路线；终点不晚于起点时不产生播放段。</summary>
    /// <param name="start">谱面起点，单位为秒。</param>
    /// <param name="end">谱面终点，单位为秒。</param>
    /// <returns>以原速播放的单段或空路线。</returns>
    public static ChartPlaybackRoute Identity(double start, double end) => new(start, end);
    /// <summary>从控制事件展开连续播放段及其播放起点。</summary>
    /// <param name="map">速度与跳转控制映射。</param>
    /// <param name="start">谱面起点，单位为秒。</param>
    /// <param name="end">谱面终点，单位为秒。</param>
    /// <exception cref="InvalidDataException">展开后超过一万段或二十四小时。</exception>
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
            legs.Add(new(cursor, next, wall, rate));
            wall += (next - cursor) / rate;
            // 互相跳转的控制可以构成无限循环，用段数和总时长两个上限强制终止，而不是让加载卡死。
            if (legs.Count > 10000 || wall > 86400) throw new InvalidDataException("Music-control route exceeds 10000 segments or 24 hours.");
            cursor = next;
        }
    }
    /// <summary>二分定位播放秒所在的 leg；依赖 legs 按 PlaybackStart 升序，这由构造顺序保证。</summary>
    /// <param name="playback">播放秒数。</param>
    /// <returns>对应的路线段索引。</returns>
    public int IndexAt(double playback)
    {
        int lo = 0, hi = legs.Count;
        while (lo < hi)
        {
            int mid = (lo + hi) / 2;
            if (legs[mid].PlaybackStart <= playback) lo = mid + 1;
            else hi = mid;
        }
        return Math.Max(0, lo - 1);
    }
    /// <summary>播放秒 → 谱面秒。走完整条路线后固定停在 end，不再外推。</summary>
    /// <param name="playback">播放秒数。</param>
    /// <returns>对应的谱面秒数，路线结束后取终点。</returns>
    public double ChartAt(double playback)
    {
        if (legs.Count == 0 || playback >= Duration) return end;
        var leg = legs[IndexAt(playback)];
        return leg.Start + Math.Max(0, playback - leg.PlaybackStart) * leg.Rate;
    }
    /// <summary>取得指定播放秒数所落入段的倍率。</summary>
    /// <param name="playback">播放秒数。</param>
    /// <returns>当前段的速度倍率，空路线时为 1。</returns>
    public double RateAt(double playback) => legs.Count == 0 ? 1 : legs[IndexAt(playback)].Rate;
}
