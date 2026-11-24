namespace KuroakiGimmick.Core;

/// <summary>支持跳转的 Pictured 控制重放，包括自动复位的控制项及持续存在的方块生成器。</summary>
public sealed class SpriteDialogueState
{
    /// <summary>图片状态的一次离散变化。</summary>
    /// <param name="Time">变更生效时刻，单位为秒。</param>
    /// <param name="Frame">图片帧号。</param>
    /// <param name="Visible">是否显示图片。</param>
    public readonly record struct Picture(double Time, int Frame, bool Visible);
    /// <summary>方块效果的一次生成事件。</summary>
    /// <param name="Time">触发时刻，单位为秒。</param>
    /// <param name="Count">生成的方块数。</param>
    /// <param name="Seed">确定性随机种子。</param>
    public readonly record struct Cubes(double Time, int Count, uint Seed);
    /// <summary>图片精灵的帧号与可见性变化记录。</summary>
    public List<Picture> Pictures { get; } = [new(double.MinValue, 0, false)];
    /// <summary>场景脉冲效果的触发时刻。</summary>
    public List<double> Pulses { get; } = [];
    /// <summary>按触发时刻保存的方块生成批次。</summary>
    public List<Cubes> CubeBatches { get; } = [];
    /// <summary>方块实例结束显示的时刻，未结束时为正无穷。</summary>
    public double CubeDeath { get; private set; } = double.PositiveInfinity;

    /// <summary>从时间轴预计算图片切换及方块生成事件。</summary>
    /// <param name="timeline">用于采样轨道和回调的时间轴。</param>
    public SpriteDialogueState(Timeline timeline)
    {
        int frame = 0, serial = 0;
        foreach (var (time, value) in Writes(timeline, "2_bg_pics"))
        {
            if (RoomFxState.Enabled(value))
                frame = Math.Min(26, (int)(Timeline.Hash(unchecked((uint)serial++ * 65537 + 9143)) * 27));
            // 消费正值后复位到 0；该状态仍然可见。
            Pictures.Add(new(time, frame, value >= 0));
        }
        foreach (var (time, value) in Writes(timeline, "2_dialbox_pulsar"))
            if (RoomFxState.Enabled(value)) Pulses.Add(time);
        int total = 0;
        int ticks = (int)Math.Ceiling(Math.Min(3600, timeline.End) * 60);
        for (int tick = 1; tick <= ticks; tick++)
        {
            double time = tick / 60.0, glitch = timeline.Get("1_glitch", time);
            if (glitch > 1)
            {
                // 生成器和已经生成的所有方块在此同时销毁。
                CubeDeath = time;
                break;
            }
            if (glitch <= 0) continue;
            if (total + 90 > 100_000)
            {
                timeline.Native?.Fail("pictured-cubes", "Persistent cube count exceeds the 100000-instance preview budget.");
                break;
            }
            CubeBatches.Add(new(time, 90, unchecked((uint)tick * 65537 + 3917)));
            total += 90;
        }
    }

    /// <summary>查询给定时刻最后生效的图片状态。</summary>
    /// <param name="time">采样时刻，单位为秒。</param>
    /// <returns>当前生效的图片帧和可见标志。</returns>
    public Picture PictureAt(double time)
    {
        int index = SortedSearch.UpperBound(Pictures, time, x => x.Time) - 1;
        return index < 0 ? new(0, 0, false) : Pictures[index];
    }

    // 按源逻辑时钟采样缓动写入。消费已结束的写入后，其持续终值不能再次触发复位控制。
    /// <summary>从指定全局轨道中枚举写入时刻与对应数值。</summary>
    /// <param name="timeline">用于采样轨道和回调的时间轴。</param>
    /// <param name="name">全局轨道名。</param>
    /// <returns>按来源轨道导出的时刻与数值对。</returns>
    public static IEnumerable<(double Time, double Value)> Writes(Timeline timeline, string name)
    {
        if (!timeline.Tracks.TryGetValue((name, -1), out var segments)) yield break;
        var writes = new SortedDictionary<double, double>();
        for (int i = 0; i < segments.Count; i++)
        {
            var segment = segments[i];
            double next = i + 1 < segments.Count ? segments[i + 1].Time : double.PositiveInfinity;
            double end = Math.Min(3600, Math.Min(segment.Time + segment.Duration, next));
            if (segment.Time >= next || segment.Time > 3600) continue;
            if (segment.Duration <= 0)
            {
                writes[segment.Time] = segment.To;
                continue;
            }
            for (int tick = (int)Math.Max(0, Math.Ceiling(segment.Time * 60)); tick <= Math.Floor(end * 60); tick++)
            {
                double time = tick / 60.0;
                if (time < next) writes[time] = segment.Value(time);
            }
            if (end < next && end == segment.Time + segment.Duration) writes[end] = segment.To;
        }
        foreach (var pair in writes) yield return (pair.Key, pair.Value);
    }
}
