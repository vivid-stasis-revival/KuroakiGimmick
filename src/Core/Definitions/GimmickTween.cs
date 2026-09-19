using System.Text.Json;

namespace KuroakiGimmick.Core;

/// <summary>绘制项属性的一段缓动。Property 限 x/y/scaleX/scaleY/alpha/angle；Delay 与 Duration 单位为秒，两者之和不超过 60。
/// 按 Delay 稳定排序后依次覆盖，同一属性的后一段结果覆盖前一段。</summary>
public sealed class GimmickTween
{
    public string Property { get; set; } = "alpha";
    public double Delay { get; set; }
    public double Duration { get; set; }
    public double From { get; set; }
    public double To { get; set; }
    public string Ease { get; set; } = "linear";
}

