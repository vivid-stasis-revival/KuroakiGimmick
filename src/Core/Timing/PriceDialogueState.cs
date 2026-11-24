using System.Globalization;

namespace KuroakiGimmick.Core;

/// <summary>重建 2024 年 4 月版本的价格状态，对话正文仅在刷新事件发生时重新选择。</summary>
public sealed class PriceDialogueState
{
    /// <summary>用于价格对话演出的水果价格列表。</summary>
    public IReadOnlyList<double> Fruits { get; }
    /// <summary>轨道 1 显示的香蕉价格。</summary>
    public double Banana { get; }
    readonly List<(double Time, int? Index)> refreshes = [];
    /// <summary>根据价格选择和对话刷新事件建立查询状态。</summary>
    /// <param name="timeline">用于采样轨道和回调的时间轴。</param>
    /// <param name="chart">提供音符、演出事件及诊断的谱面。</param>
    /// <param name="dialogueCount">可用对话条目数。</param>
    public PriceDialogueState(Timeline timeline, Chart chart, int dialogueCount)
    {
        uint seed = 0xAF2024;
        double Random() => Timeline.Hash(seed++);
        Fruits = Enumerable.Range(0, 4).Select(_ => (Random() >= .5 ? 2 : 6) + Random() * 2.99).ToArray();
        Banana = (Random() >= .5 ? 5 : 9) + Random() * .99;
        foreach (var callback in timeline.Callbacks.Where(c => c.Name == "refreshdialogue"))
        {
            double selected = timeline.Get("dialogue_id", callback.Time);
            int? index = Index(selected, dialogueCount);
            refreshes.Add((callback.Time, index));
            if (index == null) chart.Diagnostics.Add(new("native-multigrode", 0, "refreshdialogue selected an out-of-range source dialogue ID.", true));
        }
    }
    /// <summary>返回指定时刻最后一次刷新选中的正文索引，没有正文时为 null。</summary>
    /// <param name="time">采样时刻，单位为秒。</param>
    /// <returns>当前正文索引，没有已刷新正文时为 null。</returns>
    public int? BodyIndexAt(double time)
    {
        int index = SortedSearch.UpperBound(refreshes, time, r => r.Time) - 1;
        return index < 0 ? null : refreshes[index].Index;
    }
    /// <summary>将有效范围内的有限数值转换为表索引，超出范围时返回 null。</summary>
    /// <param name="value">候选数值索引。</param>
    /// <param name="count">对话表长度。</param>
    /// <returns>范围内的整数索引，无效时为 null。</returns>
    public static int? Index(double value, int count) => double.IsFinite(value) && value >= 0 && value < count ? (int)value : null;
    /// <summary>以固定两位小数和不随系统变化的文化设置格式化价格。</summary>
    /// <param name="price">要显示的价格。</param>
    /// <returns>使用小数点和两位小数的价格文字。</returns>
    public static string Format(double price) => price.ToString("0.00", CultureInfo.InvariantCulture);
    /// <summary>返回对应轨道显示的下落价格，轨道 1 使用香蕉价格。</summary>
    /// <param name="lane">音符的轨道编号。</param>
    /// <returns>该轨道选用的水果价格。</returns>
    public double FallingPrice(int lane) => lane == 1 ? Banana : Fruits[lane];
    /// <summary>根据谱面时刻、视觉延迟及价格配置计算纵坐标与不透明度。</summary>
    /// <param name="timeline">用于采样轨道和回调的时间轴。</param>
    /// <param name="project">当前工程的路径与呈现配置。</param>
    /// <param name="time">采样时刻，单位为秒。</param>
    /// <returns>下落价格文字的纵坐标与不透明度。</returns>
    public static (double Y, double Alpha) FallingPricePose(Timeline timeline, ViewerProject project, double time)
    {
        double M(string name) => timeline.Get(name, time);
        double distance = (timeline.Native?.Data?.Behavior("price-dialogue")?.Number("fallingTimeMs", 239046) ?? 239046) - time * 1000 + project.VisualDelayMs;
        double scroll = M("scrollspeed") * M("velocity"), bps = timeline.Bpm.BpmAtBeat(timeline.Bpm.Beat(time)) / 60;
        double y = NoteMotion.YFromScroll(distance, M("yoffset"), M("driven"), bps, scroll, M("wave"));
        double boost = M("boost_distance"), boostTime = M("boost_time");
        if (boost != 0) y += -boost + (distance < boostTime && boostTime > 0 ? boost * Math.Pow((boostTime - distance) / boostTime, 3) : 0);
        y -= 7 * project.NoteAlignment;
        double target = 144 - M("driven") * bps * scroll * 22;
        return (y - 10, y < target ? Math.Clamp((target - y) / 30, 0, 1) : 0);
    }
}
