using System.Text.Json;

namespace KuroakiGimmick.Core;

/// <summary>回调取值的闭区间约束；Integer 为 true 时还要求整数。非有限值一律拒绝，不做截断或钳制。</summary>
public sealed class GimmickCallbackConstraint
{
    /// <summary>允许的最小回调值，包含端点。</summary>
    public double Minimum { get; set; }
    /// <summary>允许的最大回调值，包含端点。</summary>
    public double Maximum { get; set; }
    /// <summary>为 true 时仅接受无小数部分的值。</summary>
    public bool Integer { get; set; }
    /// <summary>检查回调值的有限性、闭区间及可选整数约束。</summary>
    /// <param name="value">待检查的回调值。</param>
    /// <returns>所有约束均满足时为 true。</returns>
    public bool Accepts(double value) => double.IsFinite(value) && value >= Minimum && value <= Maximum && (!Integer
        || value == Math.Truncate(value));
}

