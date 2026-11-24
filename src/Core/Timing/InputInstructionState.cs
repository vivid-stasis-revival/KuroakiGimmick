namespace KuroakiGimmick.Core;

/// <summary>模拟原版 Step 轮询和复位；正在进行的缓动可以在下一 tick 再次写入正值。</summary>
public sealed class InputInstructionState
{
    /// <summary>教程提示在轨道写入之后的闪烁状态。</summary>
    /// <param name="Time">状态生效的歌曲秒数。</param>
    /// <param name="BlipBirth">闪烁开始的歌曲秒数，无活动闪烁时为 null。</param>
    public sealed record Change(double Time, double? BlipBirth);
    /// <summary>教程闪烁提示的状态变化表。</summary>
    public List<Change> Changes { get; } = [];
    /// <summary>教程遮盖层销毁的秒数，未触发时为正无穷。</summary>
    public double CoverDestroyedAt { get; private set; } = double.PositiveInfinity;

    /// <summary>根据教程控制轨道预计算封面销毁和闪烁提示时刻。</summary>
    /// <param name="timeline">用于采样轨道和回调的时间轴。</param>
    /// <param name="duration">持续时间，单位为秒。</param>
    public InputInstructionState(Timeline timeline, double duration)
    {
        var spawn = timeline.Tracks.GetValueOrDefault(("sg_endblip", -1)) ?? [];
        var destroy = timeline.Tracks.GetValueOrDefault(("sg_endblip_destroy", -1)) ?? [];
        int spawnIndex = -1, destroyIndex = -1;
        double? birth = null;
        bool PositiveWrite(List<Timeline.Segment> track, double time, ref int cursor)
        {
            int before = cursor;
            while (cursor + 1 < track.Count && track[cursor + 1].Time <= time) cursor++;
            if (cursor < 0) return false;
            var value = track[cursor];
            bool written = before != cursor || value.Duration > 0 && time <= value.Time + value.Duration;
            return written && value.Value(time) > 0;
        }
        for (int tick = 0; tick <= Math.Ceiling(Math.Clamp(duration, 0, 3600) * 60); tick++)
        {
            double time = tick / 60.0;
            if (double.IsPositiveInfinity(CoverDestroyedAt) && timeline.Get("cover1", time) > 0) CoverDestroyedAt = time;
            bool spawned = PositiveWrite(spawn, time, ref spawnIndex);
            bool destroyed = PositiveWrite(destroy, time, ref destroyIndex);
            if (spawned) birth = time;
            if (destroyed) birth = null; // 源 Step 先生成再销毁，与谱面中的声明顺序无关。
            if (spawned || destroyed) Changes.Add(new(time, birth));
        }
    }
    /// <summary>查询指定时刻最近生效的闪烁起点，未生效时可为 null。</summary>
    /// <param name="time">采样时刻，单位为秒。</param>
    /// <returns>闪烁效果的起始秒数，未生效时为 null。</returns>
    public double? BlipBirthAt(double time)
    {
        int index = SortedSearch.UpperBound(Changes, time, c => c.Time) - 1;
        return index < 0 ? null : Changes[index].BlipBirth;
    }
}
