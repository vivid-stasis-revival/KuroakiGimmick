namespace KuroakiGimmick.Core;

/// <summary>原生分层演出的资源与 mod 角色表；按角色名索引，和对象名、曲名无关。</summary>
public sealed class NativeSequenceDefinition
{
    /// <summary>从剧情角色键到精灵资源键的映射。</summary>
    public Dictionary<string, string> Sprites { get; set; } = [];
    /// <summary>从逻辑角色键到实际 mod 名称的映射。</summary>
    public Dictionary<string, string> Mods { get; set; } = [];
    /// <summary>划分剧情背景各层的角色映射。</summary>
    public Dictionary<string, string> BackgroundLayers { get; set; } = [];
    /// <summary>在固定歌曲时刻触发的原版回调，独立于 mod 事件。</summary>
    public Dictionary<string, double> AutomaticTriggers { get; set; } = [];
    /// <summary>可选的 CG 局部效果，其 shader 可以不同于对象的全画面 shader。</summary>
    public GimmickShaderMode? CgEffect { get; set; }
    /// <summary>是否先绘制斩击再绘制曲绘。</summary>
    public bool SlashesBeforeJacket { get; set; }
    /// <summary>可选的曲绘显示结束时刻，单位为秒。</summary>
    public double? JacketEndTime { get; set; }
    /// <summary>由序列触发的场景动作。</summary>
    /// <param name="Time">序列动作的时间偏移，单位为秒。</param>
    /// <param name="Trigger">时间参照的触发键。</param>
    /// <param name="Action">要执行的场景动作。</param>
    public sealed record SceneAction(double Time, string Trigger, NativeInterludeAction Action);
    /// <summary>定时触发的场景操作。</summary>
    public List<SceneAction> SceneActions { get; set; } = [];
    /// <summary>由序列安排的轨道写入。</summary>
    /// <param name="Time">写入时间偏移，单位为秒。</param>
    /// <param name="Trigger">时间参照的触发键。</param>
    /// <param name="Mod">目标轨道名。</param>
    /// <param name="Value">写入的数值。</param>
    /// <param name="Condition">可选的生效条件。</param>
    public sealed record ModWrite(double Time, string Trigger, string Mod, double Value, string? Condition = null);
    /// <summary>定时写入轨道的数值与条件。</summary>
    public List<ModWrite> ModWrites { get; set; } = [];
    /// <summary><paramref name="Speed"/> 是打字机倍率，每秒 100×Speed 个字；原版剧情为 1，谱面内剧情为 0.5。</summary>
    /// <param name="Time">提示的时间偏移，单位为秒。</param>
    /// <param name="Trigger">时间参照的触发键。</param>
    /// <param name="Speaker">说话人名称。</param>
    /// <param name="Text">要显示的台词。</param>
    /// <param name="Speed">打字机速率倍率。</param>
    /// <param name="FormatPlayerName">是否替换文本中的玩家名。</param>
    /// <param name="Clear">是否清除已有台词。</param>
    public sealed record StoryCue(double Time, string Trigger, string Speaker, string Text, double Speed = 1,
        bool FormatPlayerName = false, bool Clear = false);
    /// <summary>对话出现或清除的提示序列。</summary>
    public List<StoryCue> Story { get; set; } = [];
    /// <summary>由剧情触发的音频片段。</summary>
    /// <param name="Path">音频资源路径。</param>
    /// <param name="Time">开始播放的时间偏移，单位为秒。</param>
    /// <param name="Trigger">时间参照的触发键。</param>
    /// <param name="Gain">播放增益。</param>
    public sealed record AudioCue(string Path, double Time, string Trigger, double Gain = 1);
    /// <summary>剧情中的定时音效及其增益。</summary>
    public List<AudioCue> AudioCues { get; set; } = [];
    /// <summary>歌曲音量的渐变区间。</summary>
    /// <param name="Time">渐变起始时间偏移，单位为秒。</param>
    /// <param name="Duration">持续秒数。</param>
    /// <param name="From">起始增益。</param>
    /// <param name="To">终止增益。</param>
    /// <param name="Trigger">时间参照的触发键。</param>
    public sealed record MusicFade(double Time, double Duration, double From, double To, string Trigger);
    /// <summary>音乐增益的渐变序列。</summary>
    public List<MusicFade> MusicFades { get; set; } = [];
    /// <summary>剧情触发的歌曲定位操作。</summary>
    /// <param name="Trigger">触发键。</param>
    /// <param name="Time">目标歌曲秒数。</param>
    /// <param name="FadeInSeconds">定位后的淡入秒数。</param>
    public sealed record SongSeek(string Trigger, double Time, double FadeInSeconds = 0);
    /// <summary>歌曲定位与淡入事件。</summary>
    public List<SongSeek> SongSeeks { get; set; } = [];
    /// <summary>
    /// <paramref name="ExitFrom"/> 与 <paramref name="ExitSeconds"/> 是退场 tween 的起点与时长。
    /// 原版剧情房间固定从 122 退场、用 1 秒；谱面内剧情从停稳位置 132 退场、用 0.8 秒。
    /// </summary>
    /// <param name="Trigger">时间参照的触发键。</param>
    /// <param name="Start">开始时刻。</param>
    /// <param name="Clear">可选的清屏时刻。</param>
    /// <param name="Destroy">可选的销毁时刻。</param>
    /// <param name="ExitFrom">退场动画的起始纵坐标。</param>
    /// <param name="ExitSeconds">退场持续秒数。</param>
    public sealed record StoryWindow(string Trigger, double Start, double? Clear = null, double? Destroy = null,
        double ExitFrom = 122, double ExitSeconds = 1);
    /// <summary>剧情文本框的出入场时间窗口。</summary>
    public List<StoryWindow> StoryWindows { get; set; } = [];
    /// <summary>可选的结尾淡出时刻。</summary>
    public double? EndFadeTime { get; set; }
    /// <summary>结尾淡出的持续秒数，默认 8。</summary>
    public double EndFadeDuration { get; set; } = 8;
    /// <summary>启动结尾淡出的可选触发键。</summary>
    public string? EndFadeTrigger { get; set; }
    /// <summary>查找角色对应的 mod 名称，无映射时返回 null。</summary>
    /// <param name="role">组件内部使用的角色键。</param>
    /// <returns>对应轨道名，未配置时为 null。</returns>
    public string? Mod(string role) => Mods.GetValueOrDefault(role);
}
