using System.Text.Json;

namespace KuroakiGimmick.Core;

/// <summary>回调取值的闭区间约束；Integer 为 true 时还要求整数。非有限值一律拒绝，不做截断或钳制。</summary>
public sealed class GimmickCallbackConstraint
{
    public double Minimum { get; set; }
    public double Maximum { get; set; }
    public bool Integer { get; set; }
    public bool Accepts(double value) => double.IsFinite(value) && value >= Minimum && value <= Maximum && (!Integer
        || value == Math.Truncate(value));
}

