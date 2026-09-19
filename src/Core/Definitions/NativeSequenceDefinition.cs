namespace KuroakiGimmick.Core;

/// <summary>原生分层演出的资源与 mod 角色表；按角色名索引，和对象名、曲名无关。</summary>
public sealed class NativeSequenceDefinition
{
    public Dictionary<string, string> Sprites { get; set; } = [];
    public Dictionary<string, string> Mods { get; set; } = [];
    public Dictionary<string, string> BackgroundLayers { get; set; } = [];
    public sealed record StoryCue(double Time, string Trigger, string Speaker, string Text);
    public List<StoryCue> Story { get; set; } = [];
    public sealed record StoryWindow(string Trigger, double Start, double? Clear = null, double? Destroy = null);
    public List<StoryWindow> StoryWindows { get; set; } = [];
    public double? EndFadeTime { get; set; }
    public double EndFadeDuration { get; set; } = 8;
    public string? EndFadeTrigger { get; set; }
    public string? Mod(string role) => Mods.GetValueOrDefault(role);
}
