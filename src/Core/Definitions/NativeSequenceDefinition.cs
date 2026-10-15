namespace KuroakiGimmick.Core;

/// <summary>原生分层演出的资源与 mod 角色表；按角色名索引，和对象名、曲名无关。</summary>
public sealed class NativeSequenceDefinition
{
    public Dictionary<string, string> Sprites { get; set; } = [];
    public Dictionary<string, string> Mods { get; set; } = [];
    public Dictionary<string, string> BackgroundLayers { get; set; } = [];
    /// <summary>Source callbacks scheduled at a fixed song time, independent of mod events.</summary>
    public Dictionary<string, double> AutomaticTriggers { get; set; } = [];
    /// <summary>Optional source CG-local pass; it can differ from the object's application-surface shader.</summary>
    public GimmickShaderMode? CgEffect { get; set; }
    public bool SlashesBeforeJacket { get; set; }
    public double? JacketEndTime { get; set; }
    public sealed record SceneAction(double Time, string Trigger, NativeInterludeAction Action);
    public List<SceneAction> SceneActions { get; set; } = [];
    public sealed record ModWrite(double Time, string Trigger, string Mod, double Value, string? Condition = null);
    public List<ModWrite> ModWrites { get; set; } = [];
    /// <summary><paramref name="Speed"/> 是打字机倍率，每秒 100×Speed 个字；原版剧情为 1，谱面内剧情为 0.5。</summary>
    public sealed record StoryCue(double Time, string Trigger, string Speaker, string Text, double Speed = 1,
        bool FormatPlayerName = false, bool Clear = false);
    public List<StoryCue> Story { get; set; } = [];
    public sealed record AudioCue(string Path, double Time, string Trigger, double Gain = 1);
    public List<AudioCue> AudioCues { get; set; } = [];
    public sealed record MusicFade(double Time,double Duration,double From,double To,string Trigger);
    public List<MusicFade> MusicFades { get; set; } = [];
    public sealed record SongSeek(string Trigger,double Time,double FadeInSeconds=0);
    public List<SongSeek> SongSeeks { get; set; } = [];
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
