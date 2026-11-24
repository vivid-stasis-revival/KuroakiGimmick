namespace KuroakiGimmick.Core;

/// <summary>在缓动与时间跳转之间保留最近一次有效的离散精灵选择。</summary>
public sealed class SpriteSelectionState
{
    readonly List<(double Time, int Index)> choices = [(double.NegativeInfinity, 0)];
    /// <summary>根据来源事件预计算离散精灵选择。</summary>
    /// <param name="timeline">用于采样轨道和回调的时间轴。</param>
    /// <param name="mod">用于选择精灵的轨道名。</param>
    /// <param name="count">可选精灵数量。</param>
    public SpriteSelectionState(Timeline timeline, string mod, int count)
    {
        int index = 0;
        for (int tick = 0; tick <= Math.Ceiling(Math.Min(3600, timeline.End) * 60); tick++)
        {
            double time = tick / 60.0, value = timeline.Get(mod, time);
            if (value >= 0 && value < count && value == Math.Truncate(value) && (int)value != index) choices.Add((time, index = (int)value));
        }
    }
    /// <summary>取得指定时刻最后选中的精灵索引。</summary>
    /// <param name="time">采样时刻，单位为秒。</param>
    /// <returns>最近生效的精灵索引。</returns>
    public int At(double time) => choices[Math.Max(0, SortedSearch.UpperBound(choices, time, c => c.Time) - 1)].Index;
}
