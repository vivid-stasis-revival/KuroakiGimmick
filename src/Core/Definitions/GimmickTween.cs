using System.Text.Json;

namespace KuroakiGimmick.Core;

/// <summary>绘制项属性的一段缓动。Property 限 x/y/scaleX/scaleY/alpha/angle；Delay 与 Duration 单位为秒，两者之和不超过 60。
/// 按 Delay 稳定排序后依次覆盖，同一属性的后一段结果覆盖前一段。</summary>
public sealed class GimmickTween
{
    /// <summary>缓动控制的坐标、缩放、透明度或角度属性键。</summary>
    public string Property { get; set; } = "alpha";
    /// <summary>从绘制项触发至缓动开始的秒数。</summary>
    public double Delay { get; set; }
    /// <summary>缓动的持续秒数，0 表示直接使用终值。</summary>
    public double Duration { get; set; }
    /// <summary>缓动起点的属性值。</summary>
    public double From { get; set; }
    /// <summary>缓动终点的属性值。</summary>
    public double To { get; set; }
    /// <summary>插值曲线名，默认使用 linear。</summary>
    public string Ease { get; set; } = "linear";
}

