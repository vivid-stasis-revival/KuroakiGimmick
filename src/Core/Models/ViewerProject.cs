using System.Text.Json;

namespace KuroakiGimmick.Core;

/// <summary>
/// 可保存的项目设置。路径在内存中使用绝对路径，保存时转为项目目录的相对路径；Copy 不复制已加载资源。
/// </summary>
public sealed class ViewerProject
{
    public int Version { get; set; } = 1;
    /// <summary>仅编辑器使用的书签，存在工程文件里而不是游戏文件里。</summary>
    public List<Editing.TimelineMarker> EditorMarkers { get; set; } = [];
    public string? Chart { get; set; }
    public string? Gimmick { get; set; }
    public string? Images { get; set; }
    /// <summary>可选的显式字幕文件，按文本 ID 索引；空 ID 是旧版写法。</summary>
    public Dictionary<string, string>? TextFiles { get; set; }
    /// <summary>编辑器配套的 VSP 路径相对于 VSP 自身；普通游戏 VSP 路径仍相对于谱面。</summary>
    public bool ImagePathsRelativeToVsp { get; set; }
    /// <summary>显式指定的 ExtCustomGimmick 配置路径；编辑时保留该文件中与本项无关的其他字段。</summary>
    public string? WindowMotion { get; set; }
    public string? Audio { get; set; }
    public string? Jacket { get; set; }
    public string? FxProfile { get; set; }
    public string RoomPreset { get; set; } = "auto";
    public string? Title { get; set; }
    public string? GameUi { get; set; }
    /// <summary>VS UI 总开关。下面几项游戏 HUD 元素都在它之下，关掉它时一项都不画。</summary>
    public bool GameUiEnabled { get; set; } = true;
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
    public string? GimmickAssets { get; set; }

    /// <summary>可选的对象定义覆盖路径，与共享资源目录相互独立。</summary>
    public string? GimmickDefinition { get; set; }
    public string? SongName { get; set; }
    public string? SongArtist { get; set; }
    public string? SongLevel { get; set; }
    public double Bpm { get; set; } = 120;
    /// <summary>工程 offset，毫秒；由 BpmMap 在时间域一次性应用，不要在别处重复减。</summary>
    public double OffsetMs { get; set; }
    public double ScrollSpeed { get; set; } = 3;
    // VS 选项：0 表示音符上边缘对齐判定线，1 表示下边缘。
    public int NoteAlignment { get; set; }
    public int RenderWidth { get; set; } = 320;
    public double PreviewVolume { get; set; } = .8;
    /// <summary>用户校准延迟，毫秒；只影响音频队列与画面呈现，不进入演出时钟。</summary>
    public double AudioDelayMs { get; set; }
    public double VisualDelayMs { get; set; }
    public string Profile { get; set; } = "auto";
    public bool Notes { get; set; } = true;
    public bool PostProcessing { get; set; } = true;
    public static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };
    /// <summary>浅拷贝：集合字段单独复制以免共享可变状态；已加载的音频、纹理等资源不复制。</summary>
    public ViewerProject Copy()
    {
        var copy = (ViewerProject)MemberwiseClone();
        copy.EditorMarkers = (EditorMarkers ?? []).ToList();
        copy.TextFiles = TextFiles == null ? null : new(TextFiles, StringComparer.Ordinal);
        return copy;
    }
}
