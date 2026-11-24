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
    readonly List<Segment> segments = [];
    /// <summary>一条音乐控制：Multiplier 为倍速乘数，Target 有值表示跳转到该谱面秒数。两者互斥。</summary>
    /// <param name="Time">控制生效的谱面秒数。</param>
    /// <param name="Multiplier">速度倍率。</param>
    /// <param name="Target">可选的跳转目标时刻。</param>
    public sealed record Control(double Time, double Multiplier, double? Target);
    /// <summary>按谱面事件顺序保存的速度乘数和跳转控制。</summary>
    public IReadOnlyList<Control> Controls => controls;
    readonly List<Control> controls = [];
    /// <summary>是否至少包含一个具有目标时刻的跳转。</summary>
    public bool HasJumps => controls.Any(c => c.Target.HasValue);
    /// <summary>按拍数和源顺序整理音乐回调，建立速度分段与跳转表。</summary>
    /// <param name="chart">提供音符、演出事件及诊断的谱面。</param>
    /// <param name="bpm">在歌曲秒数与拍数之间转换的 BPM 映射。</param>
    /// <param name="enabled">是否应用音乐控制事件。</param>
    public ChartPlaybackMap(Chart chart, BpmMap bpm, bool enabled)
    {
        double rate = 1;
        var musicEvents = enabled ? chart.Mods
            .Where(e => ModCatalog.MusicControlName(e.Name) is "playspeed" or "jumpto_beat" or "jumpto_s")
            .Select(e => e with { Name = ModCatalog.MusicControlName(e.Name) })
            .OrderBy(e => e.Beat).ThenBy(e => e.Order).ToArray() : [];
        // 按拍、再按声明顺序取事件：同一拍上的多条控制必须保持作者顺序，乘法不可交换到别的结果。
        // 事件的拍在此处经 BPM map 换算成秒，之后整条链路都用秒。
        foreach (var e in musicEvents)
        {
            double at = bpm.Time(e.Beat);
            if (e.Name == "playspeed") controls.Add(new(at, Math.Round(e.To * 100, MidpointRounding.ToEven) / 100, null));
            else
            {
                // jumpto_beat 的目标是拍，jumpto_s 的目标按 From 区分秒或毫秒；无效目标记诊断并跳过该回调，不静默夹取。
                double target = e.Name == "jumpto_beat" ? bpm.Time(e.To) : e.To / (e.From > 0 ? 1 : 1000);
                if (!double.IsFinite(target) || target < 0) chart.Diagnostics.Add(new("music-control", e.SourceLine, "Jump target must be a finite non-negative song time; callback skipped.", true));
                else controls.Add(new(at, 1, target));
            }
        }
        var events = musicEvents.Where(e => e.Name == "playspeed").ToArray();
        double Apply(ModEvent e)
        {
            double next = rate * (Math.Round(e.To * 100, MidpointRounding.ToEven) / 100);
            if (!double.IsFinite(next) || next is < .01 or > 100)
            {
                chart.Diagnostics.Add(new("playspeed", e.SourceLine, "Accumulated playspeed must be 0.01..100; invalid callback skipped.", true));
                return rate;
            }
            return next;
        }
        // 0 时刻之前的 playspeed 先全部累乘进初始 rate，第 0 段才是实际起点。
        foreach (var e in events.Where(e => bpm.Time(e.Beat) <= 0)) rate = Apply(e);
        segments.Add(new(0, 0, rate));
        foreach (var e in events.Where(e => bpm.Time(e.Beat) > 0))
        {
            double time = bpm.Time(e.Beat), playback = PlaybackAt(time);
            rate = Apply(e);
            if (segments[^1].ChartTime == time) segments[^1] = new(time, playback, rate);
            else segments.Add(new(time, playback, rate));
        }
    }
    /// <summary>二分取最后一个起点不晚于 time 的分段；依赖 segments 按两个时间轴同时升序，playback 决定用哪一个作 key。</summary>
    Segment At(double time, bool playback)
    {
        int lo = 0, hi = segments.Count;
        while (lo < hi)
        {
            int mid = (lo + hi) / 2;
            if ((playback ? segments[mid].PlaybackTime : segments[mid].ChartTime) <= time) lo = mid + 1;
            else hi = mid;
        }
        return segments[Math.Max(0, lo - 1)];
    }
    /// <summary>查询指定谱面时刻生效的速度倍率。</summary>
    /// <param name="time">采样时刻，单位为秒。</param>
    /// <returns>该谱面时刻的播放倍率。</returns>
    public double RateAt(double time) => At(time, false).Rate;
    /// <summary>谱面秒 → 播放秒（音频实际经过的时间）。</summary>
    /// <param name="time">谱面时刻，单位为秒。</param>
    /// <returns>从起点累计的播放秒数。</returns>
    public double PlaybackAt(double time)
    {
        var s = At(time, false);
        return s.PlaybackTime + (time - s.ChartTime) / s.Rate;
    }
    /// <summary>播放秒 → 谱面秒，PlaybackAt 的逆运算。</summary>
    /// <param name="time">播放时钟的秒数。</param>
    /// <returns>对应的谱面时刻。</returns>
    public double ChartAt(double time)
    {
        var s = At(time, true);
        return s.ChartTime + (time - s.PlaybackTime) * s.Rate;
    }
    /// <summary>区间的实际播放秒数；存在跳转时必须走 Route，简单相减会漏掉被跳过或重复播放的段落。</summary>
    /// <param name="start">谱面起点，单位为秒。</param>
    /// <param name="end">谱面终点，单位为秒。</param>
    /// <returns>两端之间的实际播放秒数。</returns>
    public double Duration(double start, double end) => HasJumps ? Route(start, end).Duration : PlaybackAt(end) - PlaybackAt(start);
    /// <summary>从给定起点构造包含跳转的播放路线。</summary>
    /// <param name="start">谱面起点，单位为秒。</param>
    /// <param name="end">谱面终点，单位为秒。</param>
    /// <returns>展开速度和跳转控制后的路线。</returns>
    public ChartPlaybackRoute Route(double start, double end) => new(this, start, end);
    /// <summary>把 [start,end) 按倍速变化点切成等速片段，供导出按段推进；不处理跳转。</summary>
    /// <param name="start">谱面起点，单位为秒。</param>
    /// <param name="end">谱面终点，单位为秒。</param>
    /// <returns>连续速度段的边界与倍率。</returns>
    public IEnumerable<(double Start, double End, double Rate)> Spans(double start, double end)
    {
        foreach (double boundary in segments.Select(s => s.ChartTime).Where(t => t > start && t < end).Append(end))
        {
            yield return (start, boundary, RateAt(start));
            start = boundary;
        }
    }
}
