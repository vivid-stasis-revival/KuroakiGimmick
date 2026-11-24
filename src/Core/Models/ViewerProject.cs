// 保存工程级资源路径、呈现选项与场景上下文，并为会话提供可独立编辑的副本。

using System.Text.Json;

namespace KuroakiGimmick.Core;

/// <summary>
/// 可保存的项目设置。路径在内存中使用绝对路径，保存时转为项目目录的相对路径；Copy 不复制已加载资源。
/// </summary>
public sealed class ViewerProject
{
    /// <summary>工程文件的格式版本，默认 1。</summary>
    public int Version { get; set; } = 1;
    /// <summary>仅编辑器使用的书签，存在工程文件里而不是游戏文件里。</summary>
    public List<Editing.TimelineMarker> EditorMarkers { get; set; } = [];
    /// <summary>时间轴行的显示顺序；使用稳定轨道键，不改变 VSM 源行或事件执行顺序。</summary>
    public List<string> EditorTrackOrder { get; set; } = [];
    /// <summary>当前 VSC 或 VSB 谱面路径。</summary>
    public string? Chart { get; set; }
    /// <summary>当前 VSM 演出文件路径。</summary>
    public string? Gimmick { get; set; }
    /// <summary>原始 CGMK 歌曲目录，VSM 切换为编辑副本后仍用于定位脚本。</summary>
    public string? MpfSourceRoot { get; set; }
    /// <summary>查找原始 CGMK 脚本时使用的难度键。</summary>
    public string? MpfSourceDifficulty { get; set; }
    /// <summary>图片声明文件 VSP 的路径。</summary>
    public string? Images { get; set; }
    /// <summary>可选的显式字幕文件，按文本 ID 索引；空 ID 是旧版写法。</summary>
    public Dictionary<string, string>? TextFiles { get; set; }
    /// <summary>编辑器配套的 VSP 路径相对于 VSP 自身；普通游戏 VSP 路径仍相对于谱面。</summary>
    public bool ImagePathsRelativeToVsp { get; set; }
    /// <summary>显式指定的 ExtCustomGimmick 配置路径；编辑时保留该文件中与本项无关的其他字段。</summary>
    public string? WindowMotion { get; set; }
    /// <summary>会话持有的 CGMK 配置快照，可含尚未保存的编辑值，序列化时忽略。</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    internal System.Text.Json.Nodes.JsonObject? CgmkConfigOverride { get; set; }
    /// <summary>用于播放和导出的歌曲音频路径。</summary>
    public string? Audio { get; set; }
    /// <summary>用作曲绘的图像路径。</summary>
    public string? Jacket { get; set; }
    /// <summary>启用时按 Custom Songs Mod 3.4.0 的水平方帧序列读取曲绘。</summary>
    public bool JacketAnimated { get; set; }
    /// <summary>可选的场景效果配置路径。</summary>
    public string? FxProfile { get; set; }
    /// <summary>选择房间预设的键，默认 auto。</summary>
    public string RoomPreset { get; set; } = "auto";
    /// <summary>工程标题，缺省时会话使用谱面标题。</summary>
    public string? Title { get; set; }
    /// <summary>可选的游戏 HUD 资源清单位置。</summary>
    public string? GameUi { get; set; }
    /// <summary>VS UI 总开关。下面几项游戏 HUD 元素都在它之下，关掉它时一项都不画。</summary>
    public bool GameUiEnabled { get; set; } = true;
    /// <summary>游戏 HUD 使用的字体资源键。</summary>
    public string GameUiFont { get; set; } = ViewerSettings.DefaultFont;
    /// <summary>右上角的准确分（原版 sdisp_accscore），连同它的底框一起显示或隐藏。</summary>
    public bool GameUiScore { get; set; } = true;
    /// <summary>准确分下面的 EX 分（原版 sdisp_exscore）。</summary>
    public bool GameUiExScore { get; set; } = true;
    /// <summary>长条中途节拍点的钻尘（原版 obj_pressed_holds 每个 piece 打出的那一簇）。长条头尾属于普通判定，不受它控制。</summary>
    public bool GameUiHoldEffects { get; set; } = true;
    /// <summary>顶部大号数字显示什么，对应原版 op_minusscore：0 关闭 1 连击 2 EX 分 3 准确分 4 最高分 5 准确率。</summary>
    public int GameUiCombo { get; set; } = 1;
    /// <summary>判定显示，合并了原版的 op_mod_judgement 与 op_text_judgements：0 关闭 1 现代 2 经典 3 底部文字。</summary>
    public int GameUiJudgement { get; set; } = 1;
    /// <summary>演出对象的资源位置。</summary>
    public string? GimmickAssets { get; set; }

    /// <summary>可选的对象定义覆盖路径，与共享资源目录相互独立。</summary>
    public string? GimmickDefinition { get; set; }
    /// <summary>可选的歌曲名覆盖值。</summary>
    public string? SongName { get; set; }
    /// <summary>可选的曲师署名覆盖值。</summary>
    public string? SongArtist { get; set; }
    /// <summary>可选的难度等级显示文本。</summary>
    public string? SongLevel { get; set; }
    /// <summary>原版演出分支使用的数值 song_id；文件键 chart_id 另行解析。</summary>
    public int? NativeSongId { get; set; }
    /// <summary>原版演出预览的房间上下文。</summary>
    public string? NativeRoom { get; set; }
    /// <summary>工程显式提供的曲目信息隐藏标志，预览时无需读取游戏存档。</summary>
    public bool NativeObscureInfo { get; set; }
    /// <summary>提供给原版演出的禁用暂停标志。</summary>
    public bool NativeDisablePause { get; set; }
    /// <summary>Plaudite 剧情预览使用的真结局标志。</summary>
    public bool NativePlauditeTrueEnding { get; set; }
    /// <summary>原版场景预览使用的 Betweenspace 状态。</summary>
    public bool NativeInBetweenspace { get; set; }
    /// <summary>教程场景预览的遮盖状态。</summary>
    public bool NativeTutorialCover { get; set; }
    /// <summary>可选的设备和按键提示映射。</summary>
    public NativeInputContext? NativeInput { get; set; }
    /// <summary>原版剧情预览使用的玩家与进度信息。</summary>
    public NativeStoryContext? NativeStoryContext { get; set; }
    /// <summary>工程初始每分钟拍数，默认 120。</summary>
    public double Bpm { get; set; } = 120;
    /// <summary>工程 offset，毫秒；由 BpmMap 在时间域一次性应用，不要在别处重复减。</summary>
    public double OffsetMs { get; set; }
    /// <summary>音符滚动的内部倍率，由 NoteSpeed 访问器换算。</summary>
    public double ScrollSpeed { get; set; } = 3;
    /// <summary>游戏设置的流速；cc_Create 使用 op_note_speed / 5 + 1。保留工程中的内部倍率格式。</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public double NoteSpeed
    {
        get => Math.Clamp((ScrollSpeed - 1) * 5, 1, 20);
        set => ScrollSpeed = (double.IsFinite(value) ? Math.Clamp(value, 1, 20) : 10) / 5 + 1;
    }
    /// <summary>音符对齐判定线的边缘，0 表示上边缘，1 表示下边缘。</summary>
    public int NoteAlignment { get; set; }
    /// <summary>离屏渲染目标的像素宽度，逻辑画布保持 320×180。</summary>
    public int RenderWidth { get; set; } = 320;
    /// <summary>预览音量的线性增益，范围为 0 至 1。</summary>
    public double PreviewVolume { get; set; } = .8;
    /// <summary>用户校准延迟，毫秒；只影响音频队列与画面呈现，不进入演出时钟。</summary>
    public double AudioDelayMs { get; set; }
    /// <summary>画面呈现的校准偏移，单位为毫秒。</summary>
    public double VisualDelayMs { get; set; }
    /// <summary>整段演出相对音乐的呈现延迟；正值延后，不改变谱面原始 BPM/offset。</summary>
    public double TimelineOffsetMs { get; set; }
    /// <summary>选择演出配置的键，默认 auto。</summary>
    public string Profile { get; set; } = "auto";
    /// <summary>是否绘制音符层。</summary>
    public bool Notes { get; set; } = true;
    /// <summary>是否启用场景后处理。</summary>
    public bool PostProcessing { get; set; } = true;
    /// <summary>传给对象定义上下文规则的 JSON 键值。</summary>
    public Dictionary<string, JsonElement> GimmickContext { get; set; } = [];
    /// <summary>工程序列化选项，使用缩进、忽略属性名大小写并采用源生成的类型解析器。</summary>
    public static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        TypeInfoResolver = AppJson.Resolver
    };
    /// <summary>浅拷贝：集合字段单独复制以免共享可变状态；已加载的音频、纹理等资源不复制。</summary>
    /// <returns>独立复制编辑集合的工程配置。</returns>
    public ViewerProject Copy()
    {
        var copy = (ViewerProject)MemberwiseClone();
        copy.CgmkConfigOverride = (System.Text.Json.Nodes.JsonObject?)CgmkConfigOverride?.DeepClone();
        copy.GimmickContext = new(GimmickContext);
        copy.EditorMarkers = (EditorMarkers ?? []).ToList();
        copy.EditorTrackOrder = (EditorTrackOrder ?? []).ToList();
        copy.TextFiles = TextFiles == null ? null : new(TextFiles, StringComparer.Ordinal);
        copy.NativeInput = NativeInput is { } input ? input with
        { KeyboardLabels = input.KeyboardLabels?.ToArray(), GamepadIcons = input.GamepadIcons?.ToArray() } : null;
        return copy;
    }
}
