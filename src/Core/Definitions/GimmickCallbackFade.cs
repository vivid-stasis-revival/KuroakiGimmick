using System.Text.Json;

namespace KuroakiGimmick.Core;

/// <summary>以 Callback 首次触发时刻为起点推导的隐式 mod 值。Duration 单位为秒（0..3600），Ease 必须是已知缓动名。
/// RequireResourcePack / RequireResourceRoom 不满足时该 fade 整体不生效，而不是退化成 From。</summary>
public sealed class GimmickCallbackFade
{
    public string Callback { get; set; } = "";
    public double Duration { get; set; } = 1;
    public double From { get; set; }
    public double To { get; set; } = 1;
    public string Ease { get; set; } = "linear";
    public bool RequireResourcePack { get; set; }
    public string? RequireResourceRoom { get; set; }
}

