namespace KuroakiGimmick.Core;

/// <summary>将原版需确认推进的剧情按配置的阅读时序自动重放。</summary>
public sealed class NativeInterludeDefinition
{
    /// <summary>启动插段的事件键。</summary>
    public string Trigger { get; set; } = "";
    /// <summary>显式指定的歌曲插入时刻，单位为秒。</summary>
    public double? StartSongTime { get; set; }
    /// <summary>插段开始时使用的背景精灵键。</summary>
    public string? BackgroundSprite { get; set; }
    /// <summary>估计台词阅读时长时采用的每秒字符数。</summary>
    public double CharactersPerSecond { get; set; } = 24;
    /// <summary>由文字估算的台词持续秒数下限。</summary>
    public double MinimumCueSeconds { get; set; } = 2;
    /// <summary>在估算阅读时间后追加的停留秒数。</summary>
    public double PaddingSeconds { get; set; } = 1;
    /// <summary>按播放顺序排列的动作和台词。</summary>
    public List<NativeInterludeStep> Steps { get; set; } = [];
}

/// <summary>插段中的台词、动作及可选持续时间。</summary>
public sealed class NativeInterludeStep
{
    /// <summary>台词的说话人。</summary>
    public string Speaker { get; set; } = "";
    /// <summary>本步可选的台词，null 时仍可执行动作。</summary>
    public string? Text { get; set; }
    /// <summary>可选的显式持续秒数，省略时按台词或动作估算。</summary>
    public double? Duration { get; set; }
    /// <summary>在本步起点调度的动作。</summary>
    public List<NativeInterludeAction> Actions { get; set; } = [];
}

/// <summary>插段触发的立绘、背景、音频或播放控制命令。</summary>
public sealed class NativeInterludeAction : KenomaGMK.Runtime.Sequence.IPortraitAction
{
    /// <summary>选择背景、立绘、着色或播放控制操作的动作键。</summary>
    public string Kind { get; set; } = "";
    /// <summary>要操作的立绘或场景实例标识。</summary>
    public string? Id { get; set; }
    /// <summary>动作引用的精灵键。</summary>
    public string? Sprite { get; set; }
    /// <summary>音频等外部资源的路径。</summary>
    public string? Path { get; set; }
    /// <summary>要显示的精灵帧号。</summary>
    public int Frame { get; set; }
    /// <summary>动作给出的可选逻辑横坐标。</summary>
    public double? X { get; set; }
    /// <summary>动作给出的可选逻辑纵坐标。</summary>
    public double? Y { get; set; }
    /// <summary>绘制立绘时使用的缩放倍率。</summary>
    public double Scale { get; set; } = 1;
    /// <summary>是否水平翻转立绘。</summary>
    public bool Mirror { get; set; }
    /// <summary>可选的渐变起值。</summary>
    public double? From { get; set; }
    /// <summary>动作的渐变终值。</summary>
    public double To { get; set; } = 1;
    /// <summary>动作持续秒数。</summary>
    public double Duration { get; set; }
    /// <summary>着色动作使用的原版 BGR 颜色。</summary>
    public uint ColorBgr { get; set; } = 0xffffff;
    /// <summary>音频动作的线性增益。</summary>
    public double Gain { get; set; } = 1;
    /// <summary>是否循环播放动作关联的音频。</summary>
    public bool Loop { get; set; }
    /// <summary>立绘动作是否使用弹跳表现。</summary>
    public bool Bounce { get; set; } = true;
}
