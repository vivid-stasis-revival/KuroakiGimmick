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
    readonly KenomaGMK.Core.ChartPlaybackRoute route;
    public IReadOnlyList<Leg> Legs { get; }
    public double Duration => route.Duration;
    ChartPlaybackRoute(KenomaGMK.Core.ChartPlaybackRoute route)
    {
        this.route = route;
        Legs = route.Legs.Select(l => new Leg(l.Start,l.End,l.PlaybackStart,l.Rate)).ToArray();
    }
    public static ChartPlaybackRoute Identity(double start,double end) => new(KenomaGMK.Core.ChartPlaybackRoute.Identity(start,end));
    public ChartPlaybackRoute(ChartPlaybackMap map,double start,double end) : this(map.Map.Route(start,end)) { }
    public int IndexAt(double playback) => route.IndexAt(playback);
    public double ChartAt(double playback) => route.ChartAt(playback);
    public double RateAt(double playback) => route.RateAt(playback);
}
