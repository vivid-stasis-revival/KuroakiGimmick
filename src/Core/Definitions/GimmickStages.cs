using System.Text.Json;

namespace KuroakiGimmick.Core;

/// <summary>固定的渲染合成接口。增加歌曲不增加阶段；新增渲染能力才增加阶段。</summary>
public static class GimmickStages
{
    public const string BeforeRails = "before-rails";
    public const string AfterRails = "after-rails";
    public const string BeforePlayfield = "before-playfield";
    public const string BeforePost = "before-post";
    public const string FixedJudgment = "fixed-judgment";
    public const string Gui = "gui";
    public const string AfterEffects = "after-effects";
    public const string BeforeHoldOverlay = "before-hold-overlay";
    public const string BeforeNotes = "before-notes";
    public static readonly string[] All = [BeforeRails, AfterRails, BeforePlayfield, FixedJudgment, BeforeHoldOverlay, BeforeNotes, BeforePost, AfterEffects, Gui];
}

