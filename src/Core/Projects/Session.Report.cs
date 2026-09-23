using System.Text.Json;

namespace KuroakiGimmick.Core;

/// <summary>
/// 会话装配根：按依赖顺序加载谱面、对象定义和资源，再构建同一条可随机访问的时间轴。CPU 资源不持有 GPU 对象。
/// </summary>
public sealed partial class Session
{
    /// <summary>
    /// 输出 schema2 报告：记录定义/资源来源、兼容迁移与诊断。解析成功不等于渲染成功——绘制前调用时诊断里不会有真实 shader 错误，
    /// 只有绘制之后再调用才可能包含。<paramref name="time"/> 单位是秒，给出时额外求值该时刻的 fx_red 与前景层可见性。
    /// </summary>
    public string Report(double? time = null) => JsonSerializer.Serialize(new
    {
        reportSchemaVersion = 2,
        version = Paths.Version,
        build = Paths.BuildRevision,
        title = Title,
        objectName = Chart.ObjectName,
        assetsRoot = Paths.Assets,
        proxies = Chart.Proxies,
        rendering = new
        {
            width = Project.RenderWidth,
            height = Project.RenderWidth * 9 / 16,
            Project.NoteAlignment,
            Project.VisualDelayMs,
            Project.AudioDelayMs,
            Project.PreviewVolume
        },
        notes = Chart.Notes.GroupBy(n => n.Type).ToDictionary(g => g.Key.ToString(), g => g.Count()),
        // 六项统计按原版 GetSongStats 计算；长度取解码后的音频时长，没有音频时退回谱面末尾事件。
        songStats = SongInfoCard.Build(this) is var card ? new
        {
            card.Title,
            card.Artist,
            card.DisplayDifficulty,
            card.Level,
            card.Designer,
            card.BpmDisplay,
            lengthSeconds = card.LengthSeconds,
            card.LengthDisplay,
            card.NoteCount,
            card.JacketPath,
            gimmick = new
            {
                weight = card.GimmickWeight,
                source = card.GimmickSource
            },
            rounded = new
            {
                CHIP = card.Stats.RoundedChip,
                TECH = card.Stats.RoundedTech,
                STREAM = card.Stats.RoundedStream,
                CHORD = card.Stats.RoundedChord,
                BURST = card.Stats.RoundedBurst,
                GIMMICK = card.Stats.RoundedGimmick
            },
            total = card.Stats.Total
        } : null,
        windowMotion = new
        {
            path = WindowMotion.Path ?? Project.WindowMotion,
            events = WindowMotion.Events?.Count ?? 0,
            windows = WindowMotion.Count,
            proxyBindings = WindowMotion.Bindings?.Count ?? 0,
            timeUnit = "seconds",
            editorTimelineUnit = "beats via the session BPM map",
            lifecyclePreview = "song-time motion only; no pause/FCAC/results simulation"
        },
        modCount = Chart.Mods.Count,
        perFrameCount = Chart.PerFrame.Count,
        duration = Duration,
        playbackDuration = Playback.Duration(0, Duration),
        chartSpeed = time.HasValue ? Playback.RateAt(time.Value) : Playback.RateAt(0),
        playspeedSegments = Playback.Segments,
        musicControls = Playback.Controls,
        evaluationTime = time,
        eventTimeline = new
        {
            end = Timeline.End,
            audioEnd = Audio?.Duration,
            // 区间展开后 tween 越过歌曲末尾是合法的：这里只如实报出事件尾部时间，不当成不兼容，也不裁剪。
            pastAudioTail = Chart.Mods.Where(e => Audio != null && Timeline.Bpm.Time(e.Beat) + Math.Max(0,
                e.Duration) * 60 / Timeline.Bpm.BpmAtBeat(e.Beat) > Audio.Duration + 1e-6).Select(e => new
            {
                e.Name,
                e.SourceLine,
                e.Beat,
                end = Timeline.Bpm.Time(e.Beat) + Math.Max(0, e.Duration) * 60 / Timeline.Bpm.BpmAtBeat(e.Beat)
            })
        },
        sourceNoOps = Timeline.SourceNoOps,
        images = new
        {
            manifest = Images.Path,
            count = Images.Items.Count,
            textures = Images.Files.Count
        },
        texts = new
        {
            count = Texts.Tracks.Count,
            cues = Texts.Tracks.Sum(t => t.Cues.Count)
        },
        jacket = new
        {
            mode = Jackets.Mode,
            path = Jackets.DefaultPath,
            count = Jackets.Count,
            available = Jackets.Files
        },
        particles = new
        {
            starsEnabled = Stars.Enabled,
            stars = Stars.Stars.Count,
            normal = Timeline.Particles.Count(p => !p.Burst),
            burst = Timeline.Particles.Count(p => p.Burst)
        },
        noteSkin = NoteSkinProfile.Report(Paths.Assets),
        // 定义来源、兼容迁移与各组件错误都要如实留痕：缺省沿用颜色控制和显式 false 是两回事，报告里分开写。
        nativeGimmick = new
        {
            manifest = NativeGimmick.Manifest,
            definitionSource = NativeGimmick.DefinitionSource,
            definitionMigrations = NativeGimmick.DefinitionMigrations,
            nativeColorControls = NativeGimmick.UseNativeColorControls,
            declaredNativeColorControls = NativeGimmick.Data?.UseNativeColorControls,
            background = NativeGimmick.Data?.Background != null,
            postModes = NativeGimmick.Data?.PostModes.Keys.ToArray(),
            objectName = NativeGimmick.Data?.ObjectName,
            resources = NativeGimmick.Resources,
            issues = NativeGimmick.Issues,
            enabled = NativeGimmick.Data != null,
            resourceManifest = NativeGimmick.ResourceManifest,
            resourcePackLoaded = NativeGimmick.ResourcePackLoaded,
            room = NativeGimmick.ResourceRoom,
            sprites = NativeGimmick.Sprites.Keys,
            textures = NativeGimmick.TextureFiles,
            particles = Timeline.ObjectParticles.Count,
            completeObject = NativeGimmick.Data?.CompleteObject ?? false,
            callbacks = Timeline.Callbacks.Where(c => NativeGimmick.Data?.Callbacks.ContainsKey(c.Name) == true)
                .GroupBy(c => c.Name).ToDictionary(g => g.Key, g => g.Count()),
            randomness = "Fixed seed for repeatable seeks and exports; not the random sequence of a recorded game run."
        },
        checker = new
        {
            enabled = Checker.Enabled,
            available = Checker.Ready,
            refreshes = Checker.Updates.Count,
            frames = Checker.Frames
        },
        metadata = Chart.Metadata,
        gameUi = new
        {
            path = GameUi.Manifest,
            available = GameUi.Ready,
            enabled = Project.GameUiEnabled,
            font = Project.GameUiFont,
            song = Song,
            scoring = "No input judgement; score and EX start at zero. Decorative score images authored in VSP remain chart-controlled."
        },
        fx = new
        {
            path = Fx.Path,
            room = Fx.Room,
            source = Fx.Source,
            autoFallback = Fx.AutoFallback,
            association = Fx.RoomAssociation,
            preset = Project.RoomPreset,
            dynamicDefinitions = Fx.DynamicDefinitions,
            fxRed = time is { } redTime ? (double?) Timeline.Get("fx_red", redTime) : null,
            layers = Fx.Layers.Select(l => new
            {
                l.Name,
                l.Filter,
                l.Depth,
                l.Visible,
                l.Enabled,
                foregroundActive = time is { } layerTime ? (bool?) RoomFxState.IsForegroundActive(l, Chart.ObjectName == "obj_custom_gimmick",
                    NonBaseFxEnabled, NativeGimmick.UseNativeColorControls, name => Timeline.Get(name, layerTime)) : null
            })
        },
        profile = NativeGimmick.Data?.ObjectName ?? "core",
        bpm = Timeline.Bpm.Segments,
        diagnostics = Chart.Diagnostics
    }, ViewerProject.Json);
}
