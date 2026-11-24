// 管理设备偏好、最近工程与工作区预设，并负责设置的校验和持久化。

using System.Text.Json;
using System.Text.Json.Nodes;

namespace KuroakiGimmick.Core;

/// <summary>
/// 跨会话偏好设置。校验范围与默认值集中处理；工程内设置与用户默认值的应用时机由 Viewer 决定。
/// </summary>
// 设备 / 观看类默认值。工程保留自己的显式取值。
public sealed class ViewerSettings
{
    /// <summary>离屏渲染可以选择的像素宽度。</summary>
    public static readonly int[] Widths = [320, 640, 1280, 1920, 2560, 3840];
    /// <summary>检查渲染宽度，未列入支持集合的值回退为 320。</summary>
    /// <param name="value">设置中提供的候选值。</param>
    /// <returns>支持的宽度或 320。</returns>
    public static int ValidWidth(int value) => Widths.Contains(value) ? value : 320;
    // define_options + set_default_fonts：游戏里的显示名与资源名并不一致。
    // 默认是较新的 fnt_monacovs 美术资源。
    /// <summary>游戏界面默认与 Monaco 选项所对应的字体资源键。</summary>
    public const string DefaultFont = "fnt_monacovs", MonacoFont = "fnt_phosphor";
    /// <summary>将 default 和 monaco 显示别名转换成资源键，保留其他输入。</summary>
    /// <param name="value">显示名称或字体资源键。</param>
    /// <returns>对应的字体资源键。</returns>
    public static string FontResource(string value) => value.ToLowerInvariant() switch
    {
        "default" => DefaultFont,
        "monaco" => MonacoFont,
        _ => value
    };
    /// <summary>桌面停靠界面的启用开关，旧设置缺少该项时保持关闭。</summary>
    public bool DockableWorkspaceEnabled { get; set; }
    /// <summary>当前设备保存的工作区布局。</summary>
    public WorkspaceLayout Workspace { get; set; } = new();
    /// <summary>设备本地保存的命名布局预设，键以 viewer: 或 editor: 开头。</summary>
    public Dictionary<string, DockLayoutFile> WorkspacePresets { get; set; } = new(StringComparer.Ordinal);
    void NormalizeWorkspacePresets()
    {
        var safe = new Dictionary<string, DockLayoutFile>(StringComparer.Ordinal);
        foreach (var (name, file) in WorkspacePresets ?? [])
        {
            if (safe.Count >= 64) break;
            if (file == null || name.Length is < 8 or > 100 ||
                !(name.StartsWith("viewer:", StringComparison.Ordinal) && !file.Editor ||
                  name.StartsWith("editor:", StringComparison.Ordinal) && file.Editor) ||
                file.Version != 1 || file.Windows == null || file.Nodes == null ||
                file.Windows.Count > 33 || file.Nodes.Count > 128) continue;
            safe[name] = file;
        }
        WorkspacePresets = safe;
    }
    // 性能偏好保存在设备设置中。
    /// <summary>计算引擎偏好，默认 optimization。</summary>
    public string CalculationEngine { get; set; } = "optimization";
    /// <summary>音频解码引擎偏好，默认 optimization。</summary>
    public string AudioEngine { get; set; } = "optimization";
    /// <summary>是否允许并行加载资源。</summary>
    public bool ParallelLoading { get; set; } = true;
    /// <summary>是否启用解码音频缓存。</summary>
    public bool DecodedAudioCache { get; set; } = true;
    /// <summary>限制交互预览中的重复层数；旧设置缺少该项时默认开启。</summary>
    public bool StackLimit { get; set; } = true;
    /// <summary>预览重复层数允许的下限、上限与默认值。</summary>
    public const int MinStackLayers = 16, MaxStackLayers = 1024, DefaultStackLayers = 256;
    int stackLimitLayers = DefaultStackLayers;
    /// <summary>交互预览允许的重复层数，赋值时限制在 16 至 1024。</summary>
    public int StackLimitLayers
    {
        get => stackLimitLayers;
        set => stackLimitLayers = ValidStackLayers(value);
    }
    /// <summary>将重复层数限制在预览支持的区间内。</summary>
    /// <param name="value">设置中提供的候选值。</param>
    /// <returns>限幅后的重复层数。</returns>
    public static int ValidStackLayers(int value) => Math.Clamp(value, MinStackLayers, MaxStackLayers);
    /// <summary>保留 compatibility，其余输入统一使用 optimization。</summary>
    /// <param name="name">配置中的引擎键，可为 null。</param>
    /// <returns>应用回退规则后的引擎键。</returns>
    public static string ValidCalculationEngine(string? name) => name == "compatibility" ? "compatibility" : "optimization";
    /// <summary>保留 nvorbis，其余输入统一使用 optimization。</summary>
    /// <param name="name">配置中的引擎键，可为 null。</param>
    /// <returns>应用回退规则后的引擎键。</returns>
    public static string ValidAudioEngine(string? name) => name == "nvorbis" ? "nvorbis" : "optimization";
    /// <summary>是否播放界面过渡动画。</summary>
    public bool UiAnimations { get; set; } = true;
    /// <summary>界面语言偏好，默认跟随自动选择。</summary>
    public string UiLanguage { get; set; } = Core.UiLanguage.Auto;
    /// <summary>
    /// 编辑器 inspector 的字段怎么改：false（默认）= 就地编辑，光标和选区都在字段里；
    /// true = 回到传统的全屏输入框模态。多行文本和超长值无论这里怎么设都仍然走模态，单行字段放不下。
    /// </summary>
    public bool ModalValueEditor { get; set; }
    /// <summary>是否在编辑器中分组显示演出轨道。</summary>
    public bool GroupGimmickTracks { get; set; } = true;
    /// <summary>是否把重叠的时间轴事件分行显示。</summary>
    public bool SplitOverlappingTimelineClips { get; set; } = true;
    /// <summary>展开演出轨道后是否保留汇总视图。</summary>
    public bool ShowExpandedGimmickSummary { get; set; }
    /// <summary>是否启用放宽谱面加载上限的模式。</summary>
    public bool UnlimitedChartSize { get; set; }
    /// <summary>
    /// 播放中手动平移时间轴时 FOLLOW 怎么办：false（默认）= 自动关闭，把视图让给你；
    /// true = 始终 follow，平移只是暂时的，松手后仍然滑回播放头。缩放和跳转都不算手动平移。
    /// </summary>
    public bool AlwaysFollow { get; set; }
    /// <summary>所有导出共用的同名文件覆盖开关，默认关闭；不影响工程保存。</summary>
    public bool OverwriteExports { get; set; }
    /// <summary>首次运行时默认使用所有可用逻辑核心，已有设置采用保存的线程数。</summary>
    public int VideoRenderWorkers { get; set; } = ExportOptions.MaxRenderWorkers;
    /// <summary>将视频渲染工作线程数限制在 1 和可选最大值之间。</summary>
    /// <param name="value">设置中提供的候选值。</param>
    /// <returns>限幅后的工作线程数。</returns>
    public static int ValidRenderWorkers(int value) => Math.Clamp(value, 1, ExportOptions.MaxRenderWorkers);
    /// <summary>Chart Folder 导出时把 VSP 图片集中到 Assets/；默认关闭，关闭时图片直接放在谱面根目录。</summary>
    public bool PutImageGimmickIntoAssetsFolder { get; set; }
    /// <summary>当前界面主题名。</summary>
    public string UiTheme { get; set; } = "Nekomiya";
    /// <summary>可供选择的规范主题名。</summary>
    public static readonly string[] UiThemes = ["Nekomiya", "Scarlet", "Kuroaki"];
    /// <summary>不区分大小写匹配主题名，未知值回退为 Nekomiya。</summary>
    /// <param name="value">主题偏好文本。</param>
    /// <returns>规范主题名，未知值返回 Nekomiya。</returns>
    public static string ValidTheme(string? value) => UiThemes.FirstOrDefault(x => string.Equals(x, value, StringComparison.OrdinalIgnoreCase)) ?? "Nekomiya";
    /// <summary>信息卡片的导出宽度，固定 16:9。卡面按 1280x720 的逻辑坐标绘制，这里只决定实际像素密度。</summary>
    public int CardWidth { get; set; } = 1920;
    /// <summary>信息卡可以导出的像素宽度。</summary>
    public static readonly int[] CardWidths = [1280, 1920, 2560, 3840];
    /// <summary>校验卡片宽度，不支持的值回退为 1920。</summary>
    /// <param name="value">设置中提供的候选值。</param>
    /// <returns>支持的宽度或 1920。</returns>
    public static int ValidCardWidth(int value) => CardWidths.Contains(value) ? value : 1920;
    /// <summary>应用于工程的默认游戏字体资源名。</summary>
    public string GameUiFont { get; set; } = DefaultFont;
    /// <summary>VS UI 里各元素的开关，语义见 ViewerProject 上的同名字段；总开关 GameUiEnabled 属于工程，不在这里。</summary>
    public bool GameUiScore { get; set; } = true;
    /// <summary>默认是否绘制 EX 分。</summary>
    public bool GameUiExScore { get; set; } = true;
    /// <summary>默认是否绘制长条中途判定的粒子效果。</summary>
    public bool GameUiHoldEffects { get; set; } = true;
    /// <summary>默认的顶端计数显示模式。</summary>
    public int GameUiCombo { get; set; } = 1;
    /// <summary>默认判定文字或图形的显示模式。</summary>
    public int GameUiJudgement { get; set; } = 1;
    /// <summary>顶部数字与判定显示的取值上限，越界一律退回默认档；数组同时用于设置面板上的按钮文案。</summary>
    public static readonly string[] ComboModes = ["OFF", "COMBO", "EX SCORE", "ACC SCORE", "MAX SCORE", "ACCURACY"];
    /// <summary>按配置索引排列的判定显示模式标签。</summary>
    public static readonly string[] JudgementModes = ["OFF", "MODERN", "CLASSIC", "TEXT"];
    /// <summary>默认使用音符上边缘或下边缘对齐判定线。</summary>
    public int NoteAlignment { get; set; }
    /// <summary>离屏渲染分辨率的宽度，只影响画面清晰度；逻辑空间恒为 320×180，改这个不会改变任何布局或判定。</summary>
    public int RenderWidth { get; set; } = 320;
    /// <summary>应用于预览的默认线性音量。</summary>
    public double PreviewVolume { get; set; } = .8;
    /// <summary>音画对齐补偿，单位均为毫秒。AudioDelayMs 推迟声音，VisualDelayMs 推迟画面，两者相互独立。</summary>
    public double AudioDelayMs { get; set; }
    /// <summary>画面呈现的默认校准延迟，单位为毫秒。</summary>
    public double VisualDelayMs { get; set; }
    /// <summary>设备本地最近打开的工程、谱面或歌曲文件夹；不属于任何 .sgv.json 工程。</summary>
    public List<string> RecentProjects { get; set; } = [];
    /// <summary>设备本地的导出目录；不同导出类型各自记忆，不写入工程。</summary>
    public Dictionary<string, string> ExportDirectories { get; set; } = new();
    /// <summary>查找该导出类型最近使用且仍存在的目录，否则取默认输出目录。</summary>
    /// <param name="kind">导出类型键。</param>
    /// <returns>最近使用且仍存在的目录，否则为默认输出目录。</returns>
    public string ExportDirectory(string kind) => ExportDirectories != null &&
        ExportDirectories.TryGetValue(kind, out var directory) && Directory.Exists(directory) ? directory : Paths.Output;
    /// <summary>按导出类型记住输出文件的父目录。</summary>
    /// <param name="kind">导出类型键。</param>
    /// <param name="path">导出文件的目标路径。</param>
    public void RememberExportDestination(string kind, string path)
    {
        string directory = Path.GetDirectoryName(Path.GetFullPath(path))!;
        (ExportDirectories ??= new())[kind] = directory;
    }
    /// <summary>最近打开列表最多保留的条目数。</summary>
    public const int MaxRecentProjects = 10;
    static StringComparer RecentPathComparer => OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
    /// <summary>将最近路径绝对化，忽略非法项，并按当前平台路径规则去重截断。</summary>
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
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
            }
        }
        RecentProjects = normalized;
    }
    /// <summary>成功打开工程、谱面、歌曲文件夹或保存工程之后调用。</summary>
    /// <param name="path">用户最近打开或保存的路径。</param>
    /// <returns>路径被接纳并记入列表时为 true。</returns>
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
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }
    /// <summary>偏好文件固定落在日志目录下，不随工程走——它是用户级设置，不属于任何一份工程。</summary>
    public static string SettingsPath => Path.Combine(Paths.LogDirectory, "settings.json");
    /// <summary>读不到或解析失败都退回全套默认值，并把原因写到 stderr；偏好文件损坏不能阻止程序启动。</summary>
    /// <param name="path">偏好文件路径，省略时使用 SettingsPath。</param>
    /// <returns>已规范化的设置，读取失败时采用默认设置。</returns>
    public static ViewerSettings Load(string? path = null)
    {
        path ??= SettingsPath;
        try
        {
#if KG_SWITCH
            string previous = path + ".previous";
            if (!File.Exists(path) && File.Exists(previous))
                File.Move(previous, path);
#endif
            if (!File.Exists(path)) return new();
            if (new FileInfo(path).Length > 4 * 1024 * 1024)
                throw new JsonException("Settings file exceeds the safe size limit.");
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
            settings.Workspace ??= new();
            settings.Workspace.Normalize();
            settings.UiTheme = ValidTheme(settings.UiTheme);
            settings.NormalizeWorkspacePresets();
            settings.CalculationEngine = ValidCalculationEngine(settings.CalculationEngine);
            settings.AudioEngine = ValidAudioEngine(settings.AudioEngine);
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
    /// <param name="p">当前工程的路径与呈现配置。</param>
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

    /// <summary>从工程回写当前取值后落盘。先写完整 .tmp，再发布为 settings.json；Switch 保留可恢复的上一份设置。</summary>
    /// <param name="p">当前工程的路径与呈现配置。</param>
    /// <param name="path">设置文件路径，省略时使用 SettingsPath。</param>
    public void Save(ViewerProject p, string? path = null)
    {
        Workspace ??= new();
        Workspace.Normalize();
        UiTheme = ValidTheme(UiTheme);
        NormalizeWorkspacePresets();
        CalculationEngine = ValidCalculationEngine(CalculationEngine);
        AudioEngine = ValidAudioEngine(AudioEngine);
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
    /// <param name="path">设置文件路径，省略时使用 SettingsPath。</param>
    public void Persist(string? path = null)
    {
        Workspace ??= new();
        Workspace.Normalize();
        UiTheme = ValidTheme(UiTheme);
        NormalizeWorkspacePresets();
        CalculationEngine = ValidCalculationEngine(CalculationEngine);
        AudioEngine = ValidAudioEngine(AudioEngine);
        UiLanguage = Core.UiLanguage.Normalize(UiLanguage);
        NormalizeRecentProjects();
        path ??= SettingsPath;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        string temporary = path + ".tmp";
        File.WriteAllText(temporary, AppJson.Serialize(this, ViewerProject.Json));
#if KG_SWITCH
        // Horizon 的重命名无法替换已有文件；在临时设置成功发布前保留上一份完整设置。
        string previous = path + ".previous";
        if (File.Exists(path))
        {
            if (File.Exists(previous)) File.Delete(previous);
            File.Move(path, previous);
        }
        try { File.Move(temporary, path); }
        catch
        {
            if (!File.Exists(path) && File.Exists(previous)) File.Move(previous, path);
            throw;
        }
        if (File.Exists(previous)) File.Delete(previous);
#else
        File.Move(temporary, path, true);
#endif
    }
}
