using System.Text.Json;

namespace KuroakiGimmick.Core;

/// <summary>闭区间时间段，单位秒，两端都包含；验证阶段要求 Start ≤ End 且均为有限值。</summary>
public sealed class GimmickTimeRange
{
    public double Start { get; set; }
    public double End { get; set; }
    public bool Contains(double time) => time >= Start && time <= End;
}

