namespace KuroakiGimmick.Core;

/// <summary>原生分层演出的资源与 mod 角色表；按角色名索引，和对象名、曲名无关。</summary>
public sealed class NativeSequenceDefinition
{
    public Dictionary<string, string> Sprites { get; set; } = [];
    public Dictionary<string, string> Mods { get; set; } = [];
    public Dictionary<string, string> BackgroundLayers { get; set; } = [];
    /// <summary><paramref name="Speed"/> 是打字机倍率，每秒 100×Speed 个字；原版剧情为 1，谱面内剧情为 0.5。</summary>
    public sealed record StoryCue(double Time, string Trigger, string Speaker, string Text, double Speed = 1);
    public List<StoryCue> Story { get; set; } = [];
    /// <summary>
    /// <paramref name="ExitFrom"/> 与 <paramref name="ExitSeconds"/> 是退场 tween 的起点与时长。
    /// 原版剧情房间固定从 122 退场、用 1 秒；谱面内剧情从停稳位置 132 退场、用 0.8 秒。
    /// </summary>
    public sealed record StoryWindow(string Trigger, double Start, double? Clear = null, double? Destroy = null,
        double ExitFrom = 122, double ExitSeconds = 1);
    public List<StoryWindow> StoryWindows { get; set; } = [];
    public double? EndFadeTime { get; set; }
    public double EndFadeDuration { get; set; } = 8;
    public string? EndFadeTrigger { get; set; }
    public string? Mod(string role) => Mods.GetValueOrDefault(role);
}
