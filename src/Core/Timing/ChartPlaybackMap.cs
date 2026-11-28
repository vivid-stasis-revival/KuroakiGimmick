// 将 playspeed 与歌曲跳转回调解析为用于播放和导出的时钟映射。

namespace KuroakiGimmick.Core;

/// <summary>原版 playspeed 回调是对音高/速度做乘法并四舍五入到两位小数的一次性写入，不是 tween。</summary>
public sealed class ChartPlaybackMap
{
    /// <summary>谱面时间与播放时间的换算分段，Rate 为该段的倍速；两个时间轴都按秒且同时升序。</summary>
    /// <param name="ChartTime">谱面起始秒数。</param>
    /// <param name="PlaybackTime">对应的播放秒数。</param>
    /// <param name="Rate">本段速度倍率。</param>
    public sealed record Segment(double ChartTime, double PlaybackTime, double Rate);
    /// <summary>按谱面秒数和播放秒数升序排列的速度分段。</summary>
    public IReadOnlyList<Segment> Segments => segments;
    readonly IReadOnlyList<Segment> segments;
    /// <summary>一条音乐控制：Multiplier 为倍速乘数，Target 有值表示跳转到该谱面秒数。两者互斥。</summary>
    /// <param name="Time">控制生效的谱面秒数。</param>
    /// <param name="Multiplier">速度倍率。</param>
    /// <param name="Target">可选的跳转目标时刻。</param>
    public sealed record Control(double Time, double Multiplier, double? Target);
    /// <summary>按谱面事件顺序保存的速度乘数和跳转控制。</summary>
    public IReadOnlyList<Control> Controls => controls;
    readonly IReadOnlyList<Control> controls;
    internal KenomaGMK.Core.ChartPlaybackMap Map { get; }
    public bool HasJumps => Map.HasJumps;

    public ChartPlaybackMap(Chart chart, BpmMap bpm, bool enabled)
    {
        Map = new(enabled ? chart.Mods.Select(e => new KenomaGMK.Core.ChartPlaybackMap.MusicEvent(
            e.Beat,e.Order,e.SourceLine,e.Name,e.From,e.To)) : [], bpm.Time,
            d => chart.Diagnostics.Add(new(d.Source,d.Line,d.Message,d.Error)));
        segments = Map.Segments.Select(s => new Segment(s.ChartTime,s.PlaybackTime,s.Rate)).ToArray();
        controls = Map.Controls.Select(c => new Control(c.Time,c.Multiplier,c.Target)).ToArray();
    }
    public double RateAt(double time) => Map.RateAt(time);
    public double PlaybackAt(double time) => Map.PlaybackAt(time);
    public double ChartAt(double time) => Map.ChartAt(time);
    public double Duration(double start,double end) => Map.Duration(start,end);
    public ChartPlaybackRoute Route(double start,double end) => new(this,start,end);
    public IEnumerable<(double Start,double End,double Rate)> Spans(double start,double end) => Map.Spans(start,end);
}
