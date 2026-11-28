// 从谱面或对象配置构建速度分段，统一拍数、秒数与工程偏移的换算。

namespace KuroakiGimmick.Core;

/// <summary>
/// 拍数与秒之间的分段线性映射。OffsetMs 只在时间域应用一次；FixedBpm 显式忽略谱内变速，否则保留原始变速点。
/// </summary>
public sealed class BpmMap : KenomaGMK.Core.IBeatClock
{
    /// <summary>分段起点：Time 秒、Beat 拍、Bpm 为该段之后生效的速度；两个数组按 Time 与 Beat 同时升序。</summary>
    /// <param name="Time">分段起始秒数。</param>
    /// <param name="Beat">对应的起始拍。</param>
    /// <param name="Bpm">本段每分钟拍数。</param>
    public record Segment(double Time, double Beat, double Bpm) : KenomaGMK.Core.ITempoSegment;
    /// <summary>按时间和拍数排列的变速锚点。</summary>
    public List<Segment> Segments { get; } = [];
    readonly double offset;
    /// <summary>是否指定固定 BPM 并忽略谱面内的变速事件。</summary>
    public bool IsFixed { get; }
    /// <summary>依据固定速度、对象覆盖或谱内变速构建拍秒映射。</summary>
    /// <param name="chart">提供音符、演出事件及诊断的谱面。</param>
    /// <param name="bpm">初始每分钟拍数。</param>
    /// <param name="offsetMs">工程偏移，单位为毫秒。</param>
    /// <param name="fixedBpm">可选的固定 BPM。</param>
    /// <param name="nativePoints">可选的原版变速点列表。</param>
    /// <exception cref="InvalidDataException">BPM 或偏移量超限，或变速点的顺序、起点或数值无效。</exception>
    public BpmMap(Chart chart, double bpm, double offsetMs, double? fixedBpm = null,
        IReadOnlyList<GimmickBpmPoint>? nativePoints = null)
    {
        KenomaGMK.Core.BpmMap.ValidateArguments(bpm, offsetMs, fixedBpm);
        IsFixed = fixedBpm.HasValue;
        offset = offsetMs / 1000;
        // 对象变速表优先于原生谱面快路径；固定 BPM 始终覆盖对象变速表。
        if ((IsFixed || nativePoints is not { Count: > 0 }) &&
            RustEngineBridge.TryBuildBpm(chart, bpm, offsetMs, fixedBpm, out var native) && native != null)
        {
            Segments.AddRange(native);
            return;
        }
        var points = IsFixed ? null : nativePoints?.Select(p => p == null ? null! :
            new KenomaGMK.Core.GimmickBpmPoint(p.Beat, p.Bpm)).ToArray();
        var map = KenomaGMK.Core.BpmMap.FromChanges(Changes(), bpm, offsetMs, fixedBpm, points);
        Segments.EnsureCapacity(map.Segments.Count);
        foreach (var seg in map.Segments) Segments.Add(new(seg.Time, seg.Beat, seg.Bpm));

        IEnumerable<(double Time, object Value)> Changes()
        {
            foreach (var note in chart.Notes)
                if (note.Type == 3 && note.Extra.TryGetValue(1, out var value))
                    yield return (note.Time, value);
        }
    }

    /// <summary>拍 → 秒（含 offset）。二分查询最后一个起点不晚于 beat 的分段。</summary>
    /// <param name="beat">谱面位置，单位为拍。</param>
    /// <returns>应用工程偏移后的歌曲秒数。</returns>
    public double Time(double beat)
    {
        var s = KenomaGMK.Core.SegmentTiming.ByBeat(Segments, beat);
        return KenomaGMK.Core.SegmentTiming.Time(s, beat, 60) + offset;
    }

    /// <summary>秒 → 拍，是 Time 的逆运算：先减掉 offset 再折算，两边必须成对，否则 offset 会被算两次。</summary>
    /// <param name="time">歌曲时刻，单位为秒。</param>
    /// <returns>该时刻的谱面拍数。</returns>
    public double Beat(double time)
    {
        time -= offset;
        var s = KenomaGMK.Core.SegmentTiming.ByTime(Segments, time);
        return KenomaGMK.Core.SegmentTiming.Beat(s, time, 60);
    }

    /// <summary>该拍所在分段的 BPM。按拍查询不涉及 offset，因此这里不做 offset 修正。</summary>
    /// <param name="beat">谱面位置，单位为拍。</param>
    /// <returns>该拍对应的每分钟拍数。</returns>
    public double BpmAtBeat(double beat)
    {
        if (double.IsNaN(beat)) return Segments[^1].Bpm;
        var s = KenomaGMK.Core.SegmentTiming.ByBeat(Segments, beat);
        return s.Bpm;
    }
}

