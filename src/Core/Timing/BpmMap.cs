namespace KuroakiGimmick.Core;

/// <summary>
/// 拍数与秒之间的分段线性映射。OffsetMs 只在时间域应用一次；FixedBpm 显式忽略谱内变速，否则保留原始变速点。
/// </summary>
public sealed class BpmMap
{
    /// <summary>分段起点：Time 秒、Beat 拍、Bpm 为该段之后生效的速度；两个数组按 Time 与 Beat 同时升序。</summary>
    public record Segment(double Time, double Beat, double Bpm);
    public List<Segment> Segments { get; } = [];
    readonly double offset;
    public bool IsFixed { get; }
    public BpmMap(Chart chart, double bpm, double offsetMs, double? fixedBpm = null)
    {
        if (!double.IsFinite(bpm) || bpm <= 0 || bpm > 10000)
        {
            throw new InvalidDataException("BPM must be between 0 and 10000.");
        }
        if (!double.IsFinite(offsetMs) || Math.Abs(offsetMs) > 3_600_000)
        {
            throw new InvalidDataException("Offset is out of range.");
        }
        if (fixedBpm is { } fixedValue && (!double.IsFinite(fixedValue) || fixedValue <= 0 || fixedValue > 10000))
        {
            throw new InvalidDataException("Invalid fixed BPM.");
        }
        IsFixed = fixedBpm.HasValue;
        // offset 只在这里从毫秒转成秒并保存一次，Time/Beat 各自加减一次，别处不得重复应用。
        offset = offsetMs / 1000;
        Segments.Add(new(0, 0, fixedBpm ?? bpm));
        if (IsFixed)
        {
            return;
        }
        // 固定速度是刻意忽略谱内变速点的：直接返回，只留下唯一一段。
        foreach (var n in chart.Notes.Where(n => n.Type == 3).OrderBy(n => n.Time))
        {
            if (!n.Extra.TryGetValue(1, out var value))
            {
                continue;
            }
            double next = Convert.ToDouble(value);
            if (!double.IsFinite(next) || next <= 0 || next > 10000)
            {
                throw new InvalidDataException("Invalid BPM change.");
            }
            var previous = Segments[^1];
            // 负时间的变速点改写第 0 段而不新增分段：它描述的是曲子开始前就已生效的速度。
            if (n.Time < 0)
            {
                Segments[0] = new(0, 0, next);
                continue;
            }
            // 用上一段的速度把时间差折算成拍：60 秒 = BPM 拍。同一时刻的后一条覆盖前一条，不产生零长度分段。
            double beat = previous.Beat + (n.Time - previous.Time) * previous.Bpm / 60;
            if (n.Time == previous.Time)
            {
                Segments[^1] = new(n.Time, beat, next);
            }
            else
            {
                Segments.Add(new(n.Time, beat, next));
            }
        }
    }

    /// <summary>拍 → 秒（含 offset）。线性扫描取最后一个起点不晚于 beat 的分段；分段数很少，不值得二分。</summary>
    public double Time(double beat)
    {
        var s = Segments[0];
        foreach (var x in Segments)
        {
            if (x.Beat > beat)
            {
                break;
            }
            s = x;
        }
        return s.Time + (beat - s.Beat) * 60 / s.Bpm + offset;
    }

    /// <summary>秒 → 拍，是 Time 的逆运算：先减掉 offset 再折算，两边必须成对，否则 offset 会被算两次。</summary>
    public double Beat(double time)
    {
        time -= offset;
        var s = Segments[0];
        foreach (var x in Segments)
        {
            if (x.Time > time)
            {
                break;
            }
            s = x;
        }
        return s.Beat + (time - s.Time) * s.Bpm / 60;
    }

    /// <summary>该拍所在分段的 BPM。按拍查询不涉及 offset，因此这里不做 offset 修正。</summary>
    public double BpmAtBeat(double beat)
    {
        var s = Segments[0];
        foreach (var x in Segments)
        {
            if (x.Beat > beat)
            {
                break;
            }
            s = x;
        }
        return s.Bpm;
    }
}

