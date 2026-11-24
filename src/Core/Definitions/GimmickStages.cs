using System.Text.Json;

namespace KuroakiGimmick.Core;

/// <summary>固定的渲染合成接口。增加歌曲不增加阶段；新增渲染能力才增加阶段。</summary>
public static class GimmickStages
{
    /// <summary>轨道线绘制前的合成阶段。</summary>
    public const string BeforeRails = "before-rails";
    /// <summary>轨道线绘制后的合成阶段。</summary>
    public const string AfterRails = "after-rails";
    /// <summary>游戏区域绘制前的合成阶段。</summary>
    public const string BeforePlayfield = "before-playfield";
    /// <summary>后处理开始前的合成阶段。</summary>
    public const string BeforePost = "before-post";
    /// <summary>固定判定层的合成阶段。</summary>
    public const string FixedJudgment = "fixed-judgment";
    /// <summary>GUI 层的绘制阶段。</summary>
    public const string Gui = "gui";
    /// <summary>效果处理结束后的合成阶段。</summary>
    public const string AfterEffects = "after-effects";
    /// <summary>长条覆盖层绘制前的合成阶段。</summary>
    public const string BeforeHoldOverlay = "before-hold-overlay";
    /// <summary>音符绘制前的合成阶段。</summary>
    public const string BeforeNotes = "before-notes";
    /// <summary>用于识别合法阶段名的完整集合。</summary>
    public static readonly string[] All = [BeforeRails, AfterRails, BeforePlayfield, FixedJudgment, BeforeHoldOverlay, BeforeNotes, BeforePost, AfterEffects, Gui];
}

