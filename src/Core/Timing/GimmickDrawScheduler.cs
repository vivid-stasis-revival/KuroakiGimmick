namespace KuroakiGimmick.Core;

/// <summary>
/// 将已排序的回调转换为指定合成阶段的绘制命令。
/// 先按深度、再按 SortOrder 排序，同键保持回调/声明顺序；LatestOnly 只覆盖同一 drawing，不会误吞其他类型的回调。
/// </summary>
public sealed class GimmickDrawScheduler
{
    private readonly GimmickDefinition definition;
    private readonly double maximumLifetime;
    public GimmickDrawScheduler(GimmickDefinition definition)
    {
        this.definition = definition;
        // 取所有 drawing 的最长可见寿命，用作回调回溯窗口：早于 time-maximumLifetime 的回调必定已经绘制完毕。
        maximumLifetime = definition.Callbacks.Values.SelectMany(items => items).Select(drawing => drawing.Lifetime).DefaultIfEmpty(0).Max();
    }

    /// <summary>
    /// 取该时刻该阶段应绘制的命令。time 单位秒；常驻覆盖层的 Age 直接用 time，回调项的 Age 是距触发的秒数。
    /// 这里用的是 GimmickDrawing.Lifetime（可见绘制寿命），与决定时间轴尾部的 CallbackLifetimes 不是同一个量。
    /// </summary>
    public IReadOnlyList<GimmickDrawCommand> At(Timeline timeline, double time, string stage, bool notes)
    {
        var commands = new List<GimmickDrawCommand>();
        var latest = new Dictionary<GimmickDrawing, int>();
        foreach (var drawing in definition.Overlays)
        {
            if (Visible(drawing, stage, notes))
            {
                commands.Add(new(drawing, time, -1));
            }
        }
        var callbacks = timeline.Callbacks;
        // 二分找到回溯窗口的起点，再顺序扫到 time 为止；这依赖 Callbacks 按 Time 升序，是构建时保证的不变量。
        for (int index = SortedSearch.LowerBound(callbacks, time - maximumLifetime, item => item.Time); index < callbacks.Count
            && callbacks[index].Time <= time; index++)
        {
            var callback = callbacks[index];
            if (!definition.Callbacks.TryGetValue(callback.Name, out var drawings))
            {
                continue;
            }
            double age = time - callback.Time;
            foreach (var drawing in drawings)
            {
                if (!Visible(drawing, stage, notes) || age >= drawing.Lifetime || drawing.EventValue is { } value && value != callback.Value
                    || drawing.SuppressRanges.Any(range => range.Contains(callback.Time)))
                {
                    continue;
                }
                commands.Add(new(drawing, age, callback.Index));
                // LatestOnly 以 drawing 自身为键，只在同一个 drawing 内部保留最后一次触发；
                // 不同通道、不同回调各自独立，不会被这里吞掉。
                if (drawing.LatestOnly)
                {
                    latest[drawing] = callback.Index;
                }
            }
        }
        // 稳定顺序：Depth 大的先绘制，同 Depth 时 SortOrder 小的先绘制。
        // LINQ 的 OrderBy 是稳定排序，两键都相同时才靠它保留事件与声明顺序——换成不稳定排序会打乱同层合成。
        // 同深度先按显式序号排列；同深度且同序号时保持回调/声明顺序。
        // 不能只按事件时间重排：栏动画需要在侧边/粒子之后按通道顺序合成。
        return commands.Where(command => !command.Drawing.LatestOnly || command.EventIndex < 0 || latest.GetValueOrDefault(command.Drawing,
            -1) == command.EventIndex).OrderByDescending(command => command.Drawing.Depth).ThenBy(command => command.Drawing.SortOrder).ToArray();
    }

    private static bool Visible(GimmickDrawing drawing, string stage, bool notes) => drawing.Stage == stage && (!drawing.NotesOnly || notes);
}

