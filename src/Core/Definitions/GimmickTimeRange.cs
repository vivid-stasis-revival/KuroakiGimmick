using System.Text.Json;

namespace KuroakiGimmick.Core;

/// <summary>闭区间时间段，单位秒，两端都包含；验证阶段要求 Start ≤ End 且均为有限值。</summary>
public sealed class GimmickTimeRange
{
    /// <summary>包含在区间内的起始时刻。</summary>
    public double Start { get; set; }
    /// <summary>包含在区间内的结束时刻。</summary>
    public double End { get; set; }
    /// <summary>检查指定时刻是否落在闭区间内。</summary>
    /// <param name="time">与区间边界使用相同单位的时刻。</param>
    /// <returns>时刻位于两个端点之间或恰好位于端点时为 true。</returns>
    public bool Contains(double time) => time >= Start && time <= End;
}

