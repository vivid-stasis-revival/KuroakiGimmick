using System.Text.Json;
using System.Diagnostics;

namespace KuroakiGimmick.Core;

/// <summary>
/// 会话装配根：按依赖顺序加载谱面、对象定义和资源，再构建同一条可随机访问的时间轴。CPU 资源不持有 GPU 对象。
/// </summary>
public sealed partial class Session
{
    public ViewerProject Project { get; }
    public Chart Chart { get; }
    public Timeline Timeline { get; }
    /// <summary>自动演奏的判定表；命中特效与计分 HUD 共用这一份，保证画面上炸开的那一下和数字跳的那一下是同一次判定。</summary>
    public ScoreState Score { get; }
    public NoteIndex NoteIndex { get; }
    public EventDensity EventDensity { get; }
    public bool HasProxyEvents { get; }
    /// <summary>Parser fallback proxy counts do not activate the native application clear. Direct source Other_21 callers retain their explicit always-on behavior.</summary>
    public bool NativeApplicationProxyEnabled => NativeGimmick.Data is {ApplicationProxy:true,DisableProxy:false} definition
        && (definition.AlwaysApplicationProxy || Chart.Proxies > 0 && HasProxyEvents);
    readonly (string Path, long Length, long Written)? noteSourceStamp;
    readonly Chart? vscSource;
    readonly (double Bpm, double Offset, double? Fixed, string? Definition, string Profile, string NativeBpm) noteBpmSettings;
    public Windows.WindowMotionConfig WindowMotion { get; }
    public CustomImages Images { get; }
    public AudioData? Audio { get; }
    public AudioData? SourceAudio { get; }
    public AudioData? SongAudio { get; }
    public bool HasNativeSequenceAudio => !ReferenceEquals(Audio, SourceAudio);
    public NativeInterludeRoute? Interludes { get; }
    public double PresentationDuration => Interludes?.PresentationAt(Duration,true) ?? Duration;
    public CustomText Texts { get; }
    /// <summary>谱面内剧情（custom_episode）；谱面没写这个 gimmick 时为 null。</summary>
    public EpisodeScript? Episode { get; }
    public JacketAssets Jackets { get; }
    public bool DfEnabled { get; }
    public bool DistortBgEnabled { get; }
    public bool NonBaseFxEnabled { get; }
    public bool SkinChangeEnabled { get; }
    public GameFxProfile Fx { get; }
    public GameUiAssets GameUi { get; }
    public SongMetadata Song { get; }
    public Checkerboard Checker { get; }
    public NativeGimmickProfile NativeGimmick { get; }
    /// <summary>工程没有谱面、VSM 和图片时视为空会话；Viewer 仍可开，只是没有任何可演出的内容。</summary>
    public bool IsEmpty => Project.Chart == null && Project.Gimmick == null && Project.Images == null;
    /// <summary>启动时或关闭工程后使用的占位会话。它是一个完整可用的 Session，不是 null 占位，渲染与 UI 无需另写分支。</summary>
    public static Session Empty() => new(EmptyProject());

    /// <summary>空会话用的工程：关掉 notes，profile 固定 core，避免去加载任何原生 gimmick 资源。</summary>
    internal static ViewerProject EmptyProject() => new ViewerProject
    {
        Title = "Open a chart or song folder",
        Notes = false,
        Profile = "core"
    };
    public string? ProjectPath { get; private set; }
    public double Duration { get; }
    public ChartPlaybackMap Playback { get; }
    public CustomStarTimeline Stars { get; }
    public string Title => Project.Title ?? Chart.Title;

    /// <summary>先加载定义再创建时间轴，保证二进制 ID、BPM 和资源能力使用同一份配置。</summary>
    public Session(ViewerProject p, string? projectPath = null, string? editedVsm = null,
        Windows.WindowMotionConfig? editedWindows = null, Session? reuseAudioFrom = null, string? editedVsp = null, string? imageResourceRoot = null,
        IReadOnlyDictionary<string, string>? editedTexts = null)
    {
        bool loadProfile = Environment.GetEnvironmentVariable("KG_LOAD_PROFILE") == "1";
        long loadStart = Stopwatch.GetTimestamp(), lastMark = loadStart;
        void Profile(string section)
        {
            if (!loadProfile) return;
            long now = Stopwatch.GetTimestamp();
            Console.Error.WriteLine($"[K/G Load] {section,-24} {Stopwatch.GetElapsedTime(lastMark, now).TotalMilliseconds,9:F2} ms   elapsed={Stopwatch.GetElapsedTime(loadStart, now).TotalMilliseconds:F2} ms");
            lastMark = now;
        }
        Project = p;
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
            || !double.IsFinite(p.VisualDelayMs) || Math.Abs(p.VisualDelayMs) > 2000)
        {
            throw new InvalidDataException("Invalid viewer settings: alignment 0/1, supported render width, volume 0..1 and delays -2000..2000 ms required.");
        }
        // Audio is independent of the chart's scene metadata. Begin it after validation
        // and overlap the expensive decode with chart/asset metadata work.
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
            // Editor VSM/layout revisions do not alter VSC notes. Keep the pristine parser result,
            // not a previously augmented Chart (which would accumulate scene diagnostics/metadata).
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
        SourceAudio = Audio;
        Audio = NativeSequenceAudio.Mix(SourceAudio, NativeGimmick, Chart, Timeline);
        SongAudio = Audio;
        if(NativeGimmick.Data is { } nativeDefinition&&(nativeDefinition.Interludes.Count>0||nativeDefinition.Sequence?.SongSeeks.Count>0))
        {
            var route=new NativeInterludeRoute(Chart,Timeline.Bpm,nativeDefinition.Interludes,nativeDefinition.Sequence?.SongSeeks);
            if(route.HasChanges)Interludes=route;
        }
        Profile("Timeline construct");
        // 判定表依赖 BpmMap 求长条的每拍毫秒数，所以必须排在 Timeline 之后。
        noteSourceStamp = Stamp(p.Chart);
        if (noteSourceStamp != beforeNoteRead) noteSourceStamp = null; // source changed during load: never reuse a mixed snapshot
        noteBpmSettings = (p.Bpm, p.OffsetMs, NativeGimmick.Data?.FixedBpm, p.GimmickDefinition, p.Profile,
            (NativeGimmick.Data?.OffsetOverrideMs?.ToString("R",System.Globalization.CultureInfo.InvariantCulture)??"")+":"+string.Join(";", NativeGimmick.Data?.BpmOverride.Select(point =>
                FormattableString.Invariant($"{point.Beat:R}:{point.Bpm:R}")) ?? []));
        // Notes are read-only in the editor. VSM/layout revisions reuse the immutable note snapshot;
        // a changed chart file or timing configuration always rebuilds it on the existing load worker.
        bool reuseNotes = reuseAudioFrom != null && (noteSourceStamp.HasValue && reuseAudioFrom.noteSourceStamp == noteSourceStamp
            || p.Chart == null && reuseAudioFrom.Project.Chart == null)
            && reuseAudioFrom.noteBpmSettings == noteBpmSettings;
        NoteIndex = reuseNotes ? reuseAudioFrom!.NoteIndex : new(Chart.Notes, Timeline.Bpm);
        Score = reuseNotes ? reuseAudioFrom!.Score : new(Chart, Timeline.Bpm);
        EventDensity = new(Chart, Timeline.Bpm);
        // Source callbacks ignore their proxy argument. Only an evaluated proxy
        // track enables the editor's proxy view; callback metadata must not hide
        // the ordinary playfield behind an unauthored transparent proxy.
        HasProxyEvents = Timeline.Tracks.Keys.Any(static key => key.Proxy >= 0);
        Checker = Checkerboard.Load(p, Chart, Timeline);
        // 时长取音频优先；无音频时取谱面、时间轴尾部和最后一条文本 cue 之后的富余，三者取最大，避免演出被提前截断。
        Duration = Audio?.Duration ?? Math.Max(Math.Max(Chart.Duration, Timeline.End + .5),
            Texts.Tracks.SelectMany(t => t.Cues).Select(c => Timeline.Bpm.Time(c.Beat) + 4).DefaultIfEmpty(0).Max());
        Playback = new(Chart, Timeline.Bpm, SongFiles.Config(p, "ENABLE_MUSIC_CONTROL"));
        Stars = new(Timeline, Chart, Duration,
            Chart.ObjectName == "obj_sekaisen_gimmick" || Chart.ObjectName == "obj_custom_gimmick" && SongFiles.Config(p, "ENABLE_STARPARTICLE", false),
            sourceStars: Chart.ObjectName == "obj_sekaisen_gimmick");
        Audio = NativeInterludeAudio.Render(SongAudio,Interludes,NativeGimmick,Duration);
        Profile("Score/index/playback");
        // 区间展开后 tween 越过歌曲末尾是合法的。
        // 保留它并在 Report 中如实给出时间事实，这不算不兼容。
    }

    /// <summary>相对路径以 <paramref name="dir"/>（工程文件所在目录）为基准解析，不使用当前工作目录。</summary>
    static string? Resolve(string dir, string? path) => string.IsNullOrWhiteSpace(path) ? null : Path.GetFullPath(path, dir);
}
