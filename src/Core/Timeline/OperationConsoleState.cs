namespace KuroakiGimmick.Core;

/// <summary>从不可变的谱面事件采样实例生命周期，支持向后跳转。</summary>
public static class OperationConsoleState
{
    /// <summary>枚举截至给定时刻已触发的回调。</summary>
    /// <param name="timeline">用于采样轨道和回调的时间轴。</param>
    /// <param name="time">采样时刻，单位为秒。</param>
    /// <returns>已经触发的控制台事件。</returns>
    public static IEnumerable<Timeline.Callback> Events(Timeline timeline, double time) => timeline.Callbacks.Where(e => e.Time <= time);
    /// <summary>检查给定名称的回调是否已在采样时刻触发。</summary>
    /// <param name="timeline">用于采样轨道和回调的时间轴。</param>
    /// <param name="time">采样时刻，单位为秒。</param>
    /// <param name="name">目标回调名称。</param>
    /// <returns>该回调截至指定时刻已经触发时为 true。</returns>
    public static bool HasEvent(Timeline timeline, double time, string name) => Events(timeline, time).Any(e => e.Name == name);

    /// <summary>控制台剧情的背景帧。</summary>
    /// <param name="Sprite">背景精灵键。</param>
    /// <param name="Frame">当前精灵帧号。</param>
    public sealed record BackgroundFrame(string Sprite, int Frame);
    /// <summary>由最近一次匹配的背景回调确定当前精灵与帧。</summary>
    /// <param name="definition">控制台资源定义。</param>
    /// <param name="initial">初始背景精灵。</param>
    /// <param name="initialFrame">初始帧号。</param>
    /// <param name="callback">选择新背景的回调键。</param>
    /// <param name="timeline">用于采样轨道和回调的时间轴。</param>
    /// <param name="time">采样时刻，单位为秒。</param>
    /// <param name="salt">用于确定性选帧的种子偏移。</param>
    /// <returns>当前背景精灵和帧号。</returns>
    public static BackgroundFrame Background(OperationConsoleDefinition definition, string initial, int initialFrame, string callback, Timeline timeline, double time, uint salt)
    {
        var last = Events(timeline, time).LastOrDefault(e => e.Name == callback);
        if (last == null || definition.AreaBackgrounds.Length == 0) return new(initial, initialFrame);
        uint seed = unchecked((uint)last.SourceOrder * 65537u + salt);
        int index = Math.Min(definition.AreaBackgrounds.Length - 1, (int)(Timeline.Hash(seed) * definition.AreaBackgrounds.Length));
        return new(definition.AreaBackgrounds[index], definition.AreaFrames.Length == definition.AreaBackgrounds.Length ? definition.AreaFrames[index] : 0);
    }

    /// <summary>控制台画面中的一行命令。</summary>
    /// <param name="Text">命令文本。</param>
    /// <param name="Y">绘制纵坐标。</param>
    public sealed record Command(string Text, double Y);
    /// <summary>根据已触发事件生成控制台可见命令行及纵向位置。</summary>
    /// <param name="definition">控制台命令与文字表。</param>
    /// <param name="timeline">用于采样轨道和回调的时间轴。</param>
    /// <param name="time">采样时刻，单位为秒。</param>
    /// <returns>当前仍显示的命令与纵坐标。</returns>
    public static IReadOnlyList<Command> Commands(OperationConsoleDefinition definition, Timeline timeline, double time)
    {
        var active = new List<int>();
        int current = 0;
        foreach (var e in Events(timeline, time))
        {
            if (e.Name == "sm_killcommand") active.Clear();
            if (e.Name == "sm_pushcommand") active.Add(current++);
        }
        return active.Select((index, position) => new Command(index < definition.Commands.Length ? definition.Commands[index].ToUpperInvariant() : "",
            3 + (active.Count - position - 1) * 10)).Where(c => c.Y < 180).ToArray();
    }
}
