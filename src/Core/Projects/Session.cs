// 按资源依赖顺序装配谱面、演出、音轨、索引与播放映射。

using System.Text.Json;
using System.Diagnostics;

namespace KuroakiGimmick.Core;

/// <summary>
/// 会话装配根：按依赖顺序加载谱面、对象定义和资源，再构建同一条可随机访问的时间轴。CPU 资源不持有 GPU 对象。
/// </summary>
public sealed partial class Session
{
    /// <summary>本会话正在使用的工程配置。</summary>
    public ViewerProject Project { get; }
    /// <summary>已解析的音符、演出事件与诊断集合。</summary>
    public Chart Chart { get; }
    /// <summary>依据谱面、资源配置及 BPM 映射建立的可采样时间轴。</summary>
    public Timeline Timeline { get; }
    /// <summary>自动演奏的判定表；命中特效与计分 HUD 共用这一份，保证画面上炸开的那一下和数字跳的那一下是同一次判定。</summary>
    public ScoreState Score { get; }
    /// <summary>本会话音符的可见区间索引。</summary>
    public NoteIndex NoteIndex { get; }
    /// <summary>按谱面与 BPM 映射统计的事件密度。</summary>
    public EventDensity EventDensity { get; }
    /// <summary>时间轴上存在 proxy 轨道或 MPF 写入。</summary>
    public bool HasProxyEvents { get; }
    /// <summary>仅在定义启用 application proxy 且存在有效 proxy 事件或强制开启标志时启用。</summary>
    public bool NativeApplicationProxyEnabled => NativeGimmick.Data is { ApplicationProxy: true, DisableProxy: false } definition
        && (definition.AlwaysApplicationProxy || Chart.Proxies > 0 && HasProxyEvents);
    readonly (string Path, long Length, long Written)? noteSourceStamp;
    readonly Chart? vscSource;
    readonly (double Bpm, double Offset, double? Fixed, string? Definition, string Profile, string NativeBpm) noteBpmSettings;
    /// <summary>用于驱动附加窗口的动作配置。</summary>
    public Windows.WindowMotionConfig WindowMotion { get; }
    /// <summary>已解析的 VSP 图层及图片资源。</summary>
    public CustomImages Images { get; }
    /// <summary>用于实际播放的音频，包含已应用的剧情序列及插段处理。</summary>
    public AudioData? Audio { get; }
    /// <summary>用于复用的原始解码音频，未混入剧情音效。</summary>
    public AudioData? SourceAudio { get; }
    /// <summary>已混入序列音效、尚未处理插段的歌曲音频。</summary>
    public AudioData? SongAudio { get; }
    /// <summary>实际播放数据与原始解码数据是否引用不同对象。</summary>
    public bool HasNativeSequenceAudio => !ReferenceEquals(Audio, SourceAudio);
    /// <summary>可选的插段路线，用于歌曲时间与呈现时间之间的转换。</summary>
    public NativeInterludeRoute? Interludes { get; }
    /// <summary>含插段的呈现秒数，无路线时等于歌曲时长。</summary>
    public double PresentationDuration => Interludes?.PresentationAt(Duration, true) ?? Duration;
    /// <summary>从呈现时间中扣除工程演出延迟。</summary>
    /// <param name="time">呈现秒数。</param>
    /// <returns>扣除 TimelineOffsetMs 后的呈现秒数。</returns>
    public double ScenePresentationTime(double time) => time - Project.TimelineOffsetMs / 1000;
    /// <summary>将歌曲时间映射至呈现时间并应用演出延迟，再转回歌曲时间。</summary>
    /// <param name="songTime">歌曲时刻，单位为秒。</param>
    /// <returns>应用工程延迟后用于采样场景的歌曲秒数。</returns>
    public double SceneTime(double songTime)
    {
        double presentation = Interludes?.PresentationAt(songTime) ?? songTime;
        double shifted = ScenePresentationTime(presentation);
        return Interludes?.SongAt(shifted) ?? shifted;
    }
    /// <summary>已加载的字幕轨道与时间提示。</summary>
    public CustomText Texts { get; }
    /// <summary>谱面内剧情（custom_episode）；谱面没写这个 gimmick 时为 null。</summary>
    public EpisodeScript? Episode { get; }
    /// <summary>当前曲绘及其动画资源。</summary>
    public JacketAssets Jackets { get; }
    /// <summary>当前对象是否启用 DF 网格和侧线。</summary>
    public bool DfEnabled { get; }
    /// <summary>当前配置是否允许背景扭曲效果。</summary>
    public bool DistortBgEnabled { get; }
    /// <summary>当前对象启用的非基础效果开关。</summary>
    public bool NonBaseFxEnabled { get; }
    /// <summary>当前配置是否允许音符皮肤切换。</summary>
    public bool SkinChangeEnabled { get; }
    /// <summary>已解析的房间与后处理配置。</summary>
    public GameFxProfile Fx { get; }
    /// <summary>已加载的游戏 HUD 图集及字体数据。</summary>
    public GameUiAssets GameUi { get; }
    /// <summary>游戏 HUD 使用的曲目信息。</summary>
    public SongMetadata Song { get; }
    /// <summary>当前棋盘格效果的帧与更新记录。</summary>
    public Checkerboard Checker { get; }
    /// <summary>当前演出对象的配置与资源。</summary>
    public NativeGimmickProfile NativeGimmick { get; }
    /// <summary>工程没有谱面、VSM 和图片时视为空会话；Viewer 仍可开，只是没有任何可演出的内容。</summary>
    public bool IsEmpty => Project.Chart == null && Project.Gimmick == null && Project.Images == null;
    /// <summary>启动时或关闭工程后使用的占位会话。它是一个完整可用的 Session，不是 null 占位，渲染与 UI 无需另写分支。</summary>
    /// <returns>具有默认工程和有效预览状态的空会话。</returns>
    public static Session Empty() => new(EmptyProject());

    /// <summary>空会话用的工程：关掉 notes，profile 固定 core，避免去加载任何原生 gimmick 资源。</summary>
    internal static ViewerProject EmptyProject() => new ViewerProject
    {
        Title = "Open a chart or song folder",
        Notes = false,
        Profile = "core"
    };
    /// <summary>当前工程路径，尚未关联文件时可为 null。</summary>
    public string? ProjectPath { get; private set; }
    /// <summary>当前歌曲时间轴的时长，单位为秒。</summary>
    public double Duration { get; }
    /// <summary>谱面倍速与时间跳转的播放映射。</summary>
    public ChartPlaybackMap Playback { get; }
    /// <summary>已预计算的自定义星体时间轴。</summary>
    public CustomStarTimeline Stars { get; }
    /// <summary>优先采用工程标题，缺省时使用谱面标题。</summary>
    public string Title => Project.Title ?? Chart.Title;

    /// <summary>先加载定义再创建时间轴，保证二进制 ID、BPM 和资源能力使用同一份配置。</summary>
    /// <param name="p">需要装配的工程及呈现配置。</param>
    /// <param name="projectPath">关联的工程文件路径。</param>
    /// <param name="editedVsm">可选的编辑器 VSM 文本快照。</param>
    /// <param name="editedWindows">可选的窗口动作编辑快照。</param>
    /// <param name="reuseAudioFrom">提供可复用资源的已有会话。</param>
    /// <param name="editedVsp">可选的编辑器 VSP 文本快照。</param>
    /// <param name="imageResourceRoot">解析图片资源时使用的根目录。</param>
    /// <param name="editedTexts">按标识提供的字幕编辑快照。</param>
    /// <exception cref="FileNotFoundException">工程显式指定的谱面文件不存在。</exception>
    /// <exception cref="InvalidDataException">工程的流速、渲染宽度、对齐方式、音量或延迟等配置不合法。</exception>
    public Session(ViewerProject p, string? projectPath = null, string? editedVsm = null,
        Windows.WindowMotionConfig? editedWindows = null, Session? reuseAudioFrom = null, string? editedVsp = null, string? imageResourceRoot = null,
        IReadOnlyDictionary<string, string>? editedTexts = null)
    {
        bool loadProfile = Environment.GetEnvironmentVariable("KG_LOAD_PROFILE") == "1";
        using var loadLimits = ChartLoadLimits.Capture();
        long loadStart = Stopwatch.GetTimestamp(), lastMark = loadStart;
        void Profile(string section)
        {
            if (!loadProfile) return;
            long now = Stopwatch.GetTimestamp();
            Console.Error.WriteLine($"[K/G Load] {section,-24} {Stopwatch.GetElapsedTime(lastMark, now).TotalMilliseconds,9:F2} ms   elapsed={Stopwatch.GetElapsedTime(loadStart, now).TotalMilliseconds:F2} ms");
            lastMark = now;
        }
        // 1. 检查谱面路径、呈现参数和用户校准范围。
        Project = p;
        p.CgmkConfigOverride = null; // 新会话重新读取配置或接收本次编辑快照。
        ProjectPath = projectPath;
        p.GameUiFont = string.IsNullOrWhiteSpace(p.GameUiFont) ? ViewerSettings.DefaultFont : ViewerSettings.FontResource(p.GameUiFont);
        if (p.Chart != null && !File.Exists(p.Chart))
        {
            throw new FileNotFoundException("Chart file is missing.", p.Chart);
        }
        if (!double.IsFinite(p.ScrollSpeed) || p.ScrollSpeed is <= 0 or > 50)
        {
            throw new InvalidDataException("Scroll speed must be 0..50.");
        }
        if (p.NoteAlignment is < 0 or > 1 || !ViewerSettings.Widths.Contains(p.RenderWidth) || !double.IsFinite(p.PreviewVolume)
            || p.PreviewVolume is < 0 or > 1 || !double.IsFinite(p.AudioDelayMs) || Math.Abs(p.AudioDelayMs) > 2000
            || !double.IsFinite(p.VisualDelayMs) || Math.Abs(p.VisualDelayMs) > 2000
            || !double.IsFinite(p.TimelineOffsetMs) || Math.Abs(p.TimelineOffsetMs) > 2000)
        {
            throw new InvalidDataException("Invalid viewer settings: alignment 0/1, supported render width, volume 0..1 and delays -2000..2000 ms required.");
        }
        // 2. 有音轨且不能复用时，可并行启动解码，让谱面和资源解析与其重叠。
        string? audioPath = p.Audio;
        AudioData? previousAudio = reuseAudioFrom?.Project.Audio == audioPath ? reuseAudioFrom?.SourceAudio : null;
        bool parallel = OptimizationEngineSettings.ParallelLoading && audioPath != null && previousAudio == null;
        string audioEngine = OptimizationEngineSettings.AudioMode;
        bool audioCache = OptimizationEngineSettings.AudioCache;
        long audioStarted = Stopwatch.GetTimestamp();
        System.Threading.Tasks.Task<AudioData>? audioJob = parallel
            ? System.Threading.Tasks.Task.Run(() => AudioData.Decode(audioPath!, audioEngine, audioCache)) : null;
        static (string, long, long)? Stamp(string? file)
        {
            if (file == null) return null;
            var info = new FileInfo(file);
            return info.Exists ? (info.FullName, info.Length, info.LastWriteTimeUtc.Ticks) : null;
        }
        var beforeNoteRead = Stamp(p.Chart);
        if (p.Chart?.EndsWith(".vsc", StringComparison.OrdinalIgnoreCase) == true)
        {
            // VSM 与布局修改不会改变 VSC 音符。保留原始解析结果，避免复用已增补的 Chart 导致场景诊断和元数据累积。
            vscSource = beforeNoteRead.HasValue && reuseAudioFrom?.noteSourceStamp == beforeNoteRead
                ? reuseAudioFrom?.vscSource : null;
            vscSource ??= VscReader.Load(p.Chart);
            Chart = vscSource.CopyForSession();
        }
        else Chart = p.Chart == null ? new Chart { Title = p.Title ?? Path.GetFileNameWithoutExtension(p.Gimmick ?? "UNTITLED") }
            : VsbReader.Load(p.Chart, p.GimmickDefinition, p.Profile);
        // 编辑器传入的是尚未落盘的 VSM 文本，直接替换 mod 表；两条路径产出同一份 Chart，预览与保存后结果一致。
        if (editedVsm != null)
            VsmReader.ReplaceModsText(Chart, editedVsm, p.Gimmick ?? "editor.vsm");
        else if (p.Gimmick != null)
            VsmReader.ReplaceMods(Chart, p.Gimmick);
        Profile("VSC/VSB + VSM parse");
        WindowMotion = editedWindows ?? Windows.WindowMotionConfig.Load(p, Chart);
        if (editedWindows != null) editedWindows.Validate(Chart);
        p.CgmkConfigOverride = (System.Text.Json.Nodes.JsonObject)WindowMotion.Root.DeepClone();
        // 只载入资源元数据、图片尺寸和 shader 文本并按组件记录错误，此处不创建任何 GPU 纹理。
        NativeGimmick = NativeGimmickProfile.Load(p, Chart);
        Images = CustomImages.Load(p, Chart, editedVsp, imageResourceRoot);
        bool? textEnabled = WindowMotion.Root["ENABLE_TEXT"] is System.Text.Json.Nodes.JsonValue textSwitch && textSwitch.TryGetValue<bool>(out var enabled) ? enabled : null;
        Texts = CustomText.Load(p, Chart, editedTexts, textEnabled);
        // Custom Episodes 的谱面内剧情：只解析剧本并报静态问题，时刻表要量字体宽度，留到绘制侧。
        Episode = EpisodeScript.Load(p, Chart);
        foreach (var diagnostic in Episode?.Diagnostics ?? [])
        {
            Chart.Diagnostics.Add(diagnostic);
        }
        Jackets = JacketAssets.Load(p, Chart, NativeGimmick);
        DfEnabled = Chart.ObjectName == "obj_custom_gimmick" && SongFiles.Config(p, "ENABLE_DF_GRID_AND_SIDELINE");
        DistortBgEnabled = Chart.ObjectName == "obj_custom_gimmick" && SongFiles.Config(p, "ENABLE_DISTORT_BG");
        NonBaseFxEnabled = Chart.ObjectName == "obj_custom_gimmick" && SongFiles.Config(p, "ENABLE_NON_BASE_FX");
        // InitSkinChange 在 ENABLE_SKIN_CHANGE 为假时直接 exit，连 changeskin 这个 mod 都不会注册，
        // 于是谱面里写了也是死的。文档里这一项的默认值是 false，和 ENABLE_STARPARTICLE 一样按 false 兜底。
        SkinChangeEnabled = Chart.ObjectName == "obj_custom_gimmick" && SongFiles.Config(p, "ENABLE_SKIN_CHANGE", false);
        Fx = GameFxProfile.Load(p, Chart, NativeGimmick);
        Song = SongMetadata.Load(p, Chart);
        GameUi = GameUiAssets.Load(p, Chart);
        if (Texts.Tracks.Count > 0 && !GameUi.Ready)
        {
            Chart.Diagnostics.Add(new("text", 0, "Original game font pack is unavailable; text falls back to bundled Fusion Pixel 10px."));
        }
        if (p.Chart == null && p.Gimmick != null)
        {
            Chart.Diagnostics.Add(new("sources", 0,
                "No matching .vsb/.vsc was found. The lane and effects can be previewed; attach a chart for notes."));
        }
        if (NativeGimmick.Data?.InitialBpm is { } initialBpm && p.Profile == "auto")
        {
            p.Bpm = initialBpm;
        }
        Profile("Assets + scene metadata");
        if (p.Audio != null)
        {
            try
            {
                // 同一路径的音频从上一个会话直接复用：编辑器每改一次参数都重建会话，重复解码会让交互停顿。
                Audio = previousAudio ?? (audioJob != null
                    ? audioJob.GetAwaiter().GetResult() : AudioData.Decode(p.Audio, audioEngine, audioCache));
            }
            catch (Exception ex)
            {
                if (Environment.GetEnvironmentVariable("KG_RUST_AUDIO_REQUIRED") == "1") throw;
                // 音频失败不阻断预览，但记为错误诊断——strict 模式据此给出非零退出码。
                Chart.Diagnostics.Add(new("audio", 0, "Audio unavailable: " + ex.Message, true));
            }
        }
        if (loadProfile && audioPath != null)
            Console.Error.WriteLine($"[K/G Audio] engine={audioEngine} parallel={parallel} cache={audioCache} wall={Stopwatch.GetElapsedTime(audioStarted).TotalMilliseconds:F2} ms");
        Profile("Audio decode/reuse (wait)");
        Timeline = new(Chart, p, Audio?.Duration ?? Chart.Duration, Fx, NativeGimmick);
        // 3. 保存原始 PCM，再混入剧情序列，使后续会话仍可独立复用原始采样。
        SourceAudio = Audio;
        Audio = NativeSequenceAudio.Mix(SourceAudio, NativeGimmick, Chart, Timeline);
        SongAudio = Audio;
        if (NativeGimmick.Data is { } nativeDefinition && (nativeDefinition.Interludes.Count > 0 || nativeDefinition.Sequence?.SongSeeks.Count > 0))
        {
            var route = new NativeInterludeRoute(Chart, Timeline.Bpm, nativeDefinition.Interludes, nativeDefinition.Sequence?.SongSeeks);
            if (route.HasChanges) Interludes = route;
        }
        Profile("Timeline construct");
        // 判定表依赖 BpmMap 求长条的每拍毫秒数，所以必须排在 Timeline 之后。
        noteSourceStamp = Stamp(p.Chart);
        if (noteSourceStamp != beforeNoteRead) noteSourceStamp = null; // 加载期间源文件已变化，不能复用混合版本的快照。
        noteBpmSettings = (p.Bpm, p.OffsetMs, NativeGimmick.Data?.FixedBpm, p.GimmickDefinition, p.Profile,
            (NativeGimmick.Data?.OffsetOverrideMs?.ToString("R", System.Globalization.CultureInfo.InvariantCulture) ?? "") + ":" + string.Join(";", NativeGimmick.Data?.BpmOverride.Select(point =>
                FormattableString.Invariant($"{point.Beat:R}:{point.Bpm:R}")) ?? []));
        // 音符在编辑器中只读。VSM 或布局修改可复用快照；谱面或时序配置改变时，在已有加载线程上重建。
        bool reuseNotes = reuseAudioFrom != null && (noteSourceStamp.HasValue && reuseAudioFrom.noteSourceStamp == noteSourceStamp
            || p.Chart == null && reuseAudioFrom.Project.Chart == null)
            && reuseAudioFrom.noteBpmSettings == noteBpmSettings;
        NoteIndex = reuseNotes ? reuseAudioFrom!.NoteIndex : new(Chart.Notes, Timeline.Bpm);
        Score = reuseNotes ? reuseAudioFrom!.Score : new(Chart, Timeline.Bpm);
        EventDensity = new(Chart, Timeline.Bpm);
        // 原版回调忽略 proxy 参数；只有实际求值的 proxy 轨道才启用视图，以免回调元数据意外用透明 proxy 遮住玩法区域。
        HasProxyEvents = Timeline.Tracks.Keys.Any(static key => key.Proxy >= 0) || Timeline.HasMpfProxyWrites;
        Checker = Checkerboard.Load(p, Chart, Timeline);
        // 时长取音频优先；无音频时取谱面、时间轴尾部和最后一条文本 cue 之后的富余，三者取最大，避免演出被提前截断。
        Duration = Audio?.Duration ?? Math.Max(Math.Max(Chart.Duration, Timeline.End + .5),
            Texts.Tracks.SelectMany(t => t.Cues).Select(c => Timeline.Bpm.Time(c.Beat) + 4).DefaultIfEmpty(0).Max());
        Playback = Timeline.MusicPlayback;
        Stars = new(Timeline, Chart, Duration,
            NativeGimmick.HasBehavior("lane-starfield") || Chart.ObjectName == "obj_custom_gimmick" && SongFiles.Config(p, "ENABLE_STARPARTICLE", false),
            sourceStars: NativeGimmick.HasBehavior("lane-starfield"));
        Audio = NativeInterludeAudio.Render(SongAudio, Interludes, NativeGimmick, Duration);
        Profile("Score/index/playback");
        // 区间展开后 tween 越过歌曲末尾是合法的。
        // 保留它并在 Report 中如实给出时间事实，这不算不兼容。
    }

    /// <summary>相对路径以 <paramref name="dir"/>（工程文件所在目录）为基准解析，不使用当前工作目录。</summary>
    static string? Resolve(string dir, string? path) => string.IsNullOrWhiteSpace(path) ? null : Path.GetFullPath(path, dir);
}
