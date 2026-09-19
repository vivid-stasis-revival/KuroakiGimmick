using System.Text.Json;

namespace KuroakiGimmick.Core;

/// <summary>固定的渲染合成接口。增加歌曲不增加阶段；新增渲染能力才增加阶段。</summary>
public static class GimmickStages
{
    public const string BeforeRails = "before-rails";
    public const string BeforePlayfield = "before-playfield";
    public const string FixedJudgment = "fixed-judgment";
    public const string Gui = "gui";
    public static readonly string[] All = [BeforeRails, BeforePlayfield, FixedJudgment, Gui];
}

