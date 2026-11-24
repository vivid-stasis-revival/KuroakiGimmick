using System.Text.Json;

namespace KuroakiGimmick.Core;

/// <summary>以 Callback 首次触发时刻为起点推导的隐式 mod 值。Duration 单位为秒（0..3600），Ease 必须是已知缓动名。
/// RequireResourcePack / RequireResourceRoom 不满足时该 fade 整体不生效，而不是退化成 From。</summary>
public sealed class GimmickCallbackFade
{
    /// <summary>启动渐变的来源回调名。</summary>
    public string Callback { get; set; } = "";
    /// <summary>渐变持续时间，单位为秒。</summary>
    public double Duration { get; set; } = 1;
    /// <summary>渐变起始值。</summary>
    public double From { get; set; }
    /// <summary>渐变终止值。</summary>
    public double To { get; set; } = 1;
    /// <summary>在起止值之间插值的缓动函数名。</summary>
    public string Ease { get; set; } = "linear";
    /// <summary>仅在资源包可用时启用渐变。</summary>
    public bool RequireResourcePack { get; set; }
    /// <summary>可选的资源房间匹配条件。</summary>
    public string? RequireResourceRoom { get; set; }
    /// <summary>首次计划的清标记事件发生前，重复回调重新启动渐变。</summary>
    public bool RestartUntilInitialCompletion { get; set; }
}

