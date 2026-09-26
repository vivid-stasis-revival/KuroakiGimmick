using System.Text.Json;
using System.Text.Json.Nodes;

namespace KuroakiGimmick.Core;

/// <summary>
/// 跨会话偏好设置。校验范围与默认值集中处理；工程内设置与用户默认值的应用时机由 Viewer 决定。
/// </summary>
// 设备 / 观看类默认值。工程保留自己的显式取值。
public sealed class ViewerSettings
{
    public static readonly int[] Widths = [320, 640, 1280, 1920, 2560, 3840];
    public static int ValidWidth(int value) => Widths.Contains(value) ? value : 320;
    // define_options + set_default_fonts：游戏里的显示名与资源名并不一致。
    // 默认是较新的 fnt_monacovs 美术资源。
    public const string DefaultFont = "fnt_monacovs", MonacoFont = "fnt_phosphor";
    public static string FontResource(string value) => value.ToLowerInvariant() switch
    {
        "default" => DefaultFont,
        "monaco" => MonacoFont,
        _ => value
    };
    public WorkspaceLayout Workspace { get; set; } = new();
    public bool UiAnimations { get; set; } = true;
    public string UiLanguage { get; set; } = Core.UiLanguage.Auto;
    /// <summary>
    /// 编辑器 inspector 的字段怎么改：false（默认）= 就地编辑，光标和选区都在字段里；
    /// true = 回到传统的全屏输入框模态。多行文本和超长值无论这里怎么设都仍然走模态，单行字段放不下。
    /// </summary>
    public bool ModalValueEditor { get; set; }
    /// <summary>
    /// 播放中手动平移时间轴时 FOLLOW 怎么办：false（默认）= 自动关闭，把视图让给你；
    /// true = 始终 follow，平移只是暂时的，松手后仍然滑回播放头。缩放和跳转都不算手动平移。
    /// </summary>
    public bool AlwaysFollow { get; set; }
    public string UiTheme { get; set; } = "Nekomiya";
    public static readonly string[] UiThemes = ["Nekomiya", "Scarlet", "Kuroaki"];
    public static string ValidTheme(string? value) => UiThemes.FirstOrDefault(x => string.Equals(x, value, StringComparison.OrdinalIgnoreCase)) ?? "Nekomiya";
    /// <summary>信息卡片的导出宽度，固定 16:9。卡面按 1280x720 的逻辑坐标绘制，这里只决定实际像素密度。</summary>
    public int CardWidth { get; set; } = 1920;
    public static readonly int[] CardWidths = [1280, 1920, 2560, 3840];
    public static int ValidCardWidth(int value) => CardWidths.Contains(value) ? value : 1920;
    public string GameUiFont { get; set; } = DefaultFont;
    /// <summary>VS UI 里各元素的开关，语义见 ViewerProject 上的同名字段；总开关 GameUiEnabled 属于工程，不在这里。</summary>
    public bool GameUiScore { get; set; } = true;
    public bool GameUiExScore { get; set; } = true;
    public bool GameUiHoldEffects { get; set; } = true;
    public int GameUiCombo { get; set; } = 1;
    public int GameUiJudgement { get; set; } = 1;
    /// <summary>顶部数字与判定显示的取值上限，越界一律退回默认档；数组同时用于设置面板上的按钮文案。</summary>
    public static readonly string[] ComboModes = ["OFF", "COMBO", "EX SCORE", "ACC SCORE", "MAX SCORE", "ACCURACY"];
    public static readonly string[] JudgementModes = ["OFF", "MODERN", "CLASSIC", "TEXT"];
    public int NoteAlignment { get; set; }
    /// <summary>离屏渲染分辨率的宽度，只影响画面清晰度；逻辑空间恒为 320×180，改这个不会改变任何布局或判定。</summary>
    public int RenderWidth { get; set; } = 320;
    public double PreviewVolume { get; set; } = .8;
    /// <summary>音画对齐补偿，单位均为毫秒。AudioDelayMs 推迟声音，VisualDelayMs 推迟画面，两者相互独立。</summary>
    public double AudioDelayMs { get; set; }
    public double VisualDelayMs { get; set; }
    /// <summary>设备本地最近打开的工程、谱面或歌曲文件夹；不属于任何 .sgv.json 工程。</summary>
    public List<string> RecentProjects { get; set; } = [];
    /// <summary>设备本地的导出目录；不同导出类型各自记忆，不写入工程。</summary>
    public Dictionary<string, string> ExportDirectories { get; set; } = new();
    public string ExportDirectory(string kind) => ExportDirectories != null &&
        ExportDirectories.TryGetValue(kind, out var directory) && Directory.Exists(directory) ? directory : Paths.Output;
    public void RememberExportDestination(string kind, string path)
    {
        string directory = Path.GetDirectoryName(Path.GetFullPath(path))!;
        (ExportDirectories ??= new())[kind] = directory;
    }
    public const int MaxRecentProjects = 10;
    static StringComparer RecentPathComparer => OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
    public void NormalizeRecentProjects()
    {
        var seen = new HashSet<string>(RecentPathComparer);
        var normalized = new List<string>();
        foreach (var value in RecentProjects ?? [])
        {
            if (string.IsNullOrWhiteSpace(value)) continue;
            try
            {
                var path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(value));
                if (!seen.Add(path)) continue;
                normalized.Add(path);
                if (normalized.Count == MaxRecentProjects) break;
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { }
        }
        RecentProjects = normalized;
    }
    /// <summary>成功打开工程、谱面、歌曲文件夹或保存工程之后调用。</summary>
    public bool RememberRecentSource(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        try
        {
            string full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
            if (!full.EndsWith(".sgv.json", StringComparison.OrdinalIgnoreCase) &&
                Path.GetExtension(full).ToLowerInvariant() is not (".vsb" or ".vsc") && !Directory.Exists(full)) return false;
            NormalizeRecentProjects();
            RecentProjects.RemoveAll(p => RecentPathComparer.Equals(p, full));
            RecentProjects.Insert(0, full);
            if (RecentProjects.Count > MaxRecentProjects) RecentProjects.RemoveRange(MaxRecentProjects, RecentProjects.Count - MaxRecentProjects);
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { return false; }
    }
    /// <summary>偏好文件固定落在日志目录下，不随工程走——它是用户级设置，不属于任何一份工程。</summary>
    public static string SettingsPath => Path.Combine(Paths.LogDirectory, "settings.json");
    /// <summary>读不到或解析失败都退回全套默认值，并把原因写到 stderr；偏好文件损坏不能阻止程序启动。</summary>
    public static ViewerSettings Load(string? path = null)
    {
        path ??= SettingsPath;
        if (!File.Exists(path))
        {
            return new();
        }
        try
        {
            // 最近工程是可丢弃的设备历史：它坏了只清这一项，不连带丢掉音量、字体等其它偏好。
            var data = JsonNode.Parse(File.ReadAllText(path));
            string? recentKey = (data as JsonObject)?.Select(pair => pair.Key)
                .FirstOrDefault(key => string.Equals(key, nameof(RecentProjects), StringComparison.OrdinalIgnoreCase));
            if (data is JsonObject obj && recentKey != null && obj.TryGetPropertyValue(recentKey, out var recents))
            {
                if (recents is JsonArray list)
                {
                    var safe = new JsonArray();
                    foreach (var entry in list)
                        if (entry is JsonValue value && value.TryGetValue<string>(out string? item)) safe.Add((JsonNode?)JsonValue.Create(item));
                    obj[recentKey] = safe;
                }
                else obj.Remove(recentKey);
            }
            var settings = AppJson.Deserialize<ViewerSettings>(data?.ToJsonString() ?? "null", ViewerProject.Json) ?? new();
            settings.Workspace ??= new(); settings.Workspace.Normalize(); settings.UiTheme = ValidTheme(settings.UiTheme);
            settings.UiLanguage = Core.UiLanguage.Normalize(settings.UiLanguage);
            settings.NormalizeRecentProjects();
            return settings;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine("Settings could not be loaded: " + ex.Message);
            return new();
        }
    }

    /// <summary>把偏好写入工程，同时夹住取值范围：延迟 ±2000 ms、音量 0..1、对齐 0/1、渲染宽度取受支持值。命令行有覆盖时不要调用。</summary>
    public void Apply(ViewerProject p)
    {
        p.NoteAlignment = Math.Clamp(NoteAlignment, 0, 1);
        p.RenderWidth = ValidWidth(RenderWidth);
        p.GameUiFont = string.IsNullOrWhiteSpace(GameUiFont) ? DefaultFont : FontResource(GameUiFont);
        p.GameUiScore = GameUiScore;
        p.GameUiExScore = GameUiExScore;
        p.GameUiHoldEffects = GameUiHoldEffects;
        // 越界的档位退回默认，而不是夹到边界：设置文件被手改坏时"关掉"比"改成另一档"更容易被看出来。
        p.GameUiCombo = GameUiCombo >= 0 && GameUiCombo < ComboModes.Length ? GameUiCombo : 1;
        p.GameUiJudgement = GameUiJudgement >= 0 && GameUiJudgement < JudgementModes.Length ? GameUiJudgement : 1;
        p.PreviewVolume = double.IsFinite(PreviewVolume) ? Math.Clamp(PreviewVolume, 0, 1) : .8;
        p.AudioDelayMs = double.IsFinite(AudioDelayMs) ? Math.Clamp(AudioDelayMs, -2000, 2000) : 0;
        p.VisualDelayMs = double.IsFinite(VisualDelayMs) ? Math.Clamp(VisualDelayMs, -2000, 2000) : 0;
    }

    /// <summary>从工程回写当前取值后落盘。先写 .tmp 再原子替换，中途崩溃不会留下半截的 settings.json。</summary>
    public void Save(ViewerProject p, string? path = null)
    {
        Workspace ??= new(); Workspace.Normalize(); UiTheme = ValidTheme(UiTheme);
        UiLanguage = Core.UiLanguage.Normalize(UiLanguage);
        NoteAlignment = p.NoteAlignment;
        RenderWidth = p.RenderWidth;
        PreviewVolume = p.PreviewVolume;
        AudioDelayMs = p.AudioDelayMs;
        VisualDelayMs = p.VisualDelayMs;
        GameUiFont = p.GameUiFont;
        GameUiScore = p.GameUiScore;
        GameUiExScore = p.GameUiExScore;
        GameUiHoldEffects = p.GameUiHoldEffects;
        GameUiCombo = p.GameUiCombo;
        GameUiJudgement = p.GameUiJudgement;
        Persist(path);
    }

    /// <summary>只持久化用户设置对象，不从当前工程抓取预览和 Game UI 默认值。</summary>
    public void Persist(string? path = null)
    {
        Workspace ??= new(); Workspace.Normalize(); UiTheme = ValidTheme(UiTheme);
        UiLanguage = Core.UiLanguage.Normalize(UiLanguage);
        NormalizeRecentProjects();
        path ??= SettingsPath;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        string temporary = path + ".tmp";
        File.WriteAllText(temporary, AppJson.Serialize(this, ViewerProject.Json));
        File.Move(temporary, path, true);
    }
}
