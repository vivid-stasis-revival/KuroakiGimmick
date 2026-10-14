using System.Text.Json;
using System.Text.Json.Nodes;

namespace KuroakiGimmick.Core;

/// <summary>会话的机器可读兼容报告；用显式 JSON 节点保证 NativeAOT 不依赖匿名类型反射。</summary>
public sealed partial class Session
{
    /// <summary>输出 schema2 报告。time 单位为秒；给出时额外求值该时刻的 FX。</summary>
    public string Report(double? time = null)
    {
        var card = SongInfoCard.Build(this);
        var songStats = new JsonObject
        {
            ["Title"] = card.Title,
            ["Artist"] = card.Artist,
            ["DisplayDifficulty"] = card.DisplayDifficulty,
            ["Level"] = card.Level,
            ["Designer"] = card.Designer,
            ["BpmDisplay"] = card.BpmDisplay,
            ["lengthSeconds"] = card.LengthSeconds,
            ["LengthDisplay"] = card.LengthDisplay,
            ["NoteCount"] = card.NoteCount,
            ["JacketPath"] = card.JacketPath,
            ["gimmick"] = new JsonObject { ["weight"] = card.GimmickWeight, ["source"] = card.GimmickSource },
            ["rounded"] = new JsonObject
            {
                ["CHIP"] = card.Stats.RoundedChip,
                ["TECH"] = card.Stats.RoundedTech,
                ["STREAM"] = card.Stats.RoundedStream,
                ["CHORD"] = card.Stats.RoundedChord,
                ["BURST"] = card.Stats.RoundedBurst,
                ["GIMMICK"] = card.Stats.RoundedGimmick
            },
            ["total"] = card.Stats.Total
        };
        var pastAudioTail = new JsonArray(Chart.Mods.Where(e => SongAudio != null && Timeline.Bpm.Time(e.Beat) +
            Math.Max(0, e.Duration) * 60 / Timeline.Bpm.BpmAtBeat(e.Beat) > SongAudio.Duration + 1e-6)
            .Select(e => (JsonNode?)new JsonObject
            {
                ["Name"] = e.Name,
                ["SourceLine"] = e.SourceLine,
                ["Beat"] = e.Beat,
                ["end"] = Timeline.Bpm.Time(e.Beat) + Math.Max(0, e.Duration) * 60 / Timeline.Bpm.BpmAtBeat(e.Beat)
            }).ToArray());
        var fxLayers = new JsonArray(Fx.Layers.Select(l => (JsonNode?)new JsonObject
        {
            ["Name"] = l.Name,
            ["Filter"] = l.Filter,
            ["Depth"] = l.Depth,
            ["Visible"] = l.Visible,
            ["Enabled"] = l.Enabled,
            ["foregroundActive"] = JsonValue.Create(time is { } layerTime ? (bool?)RoomFxState.IsForegroundActive(l,
                Chart.ObjectName == "obj_custom_gimmick", NonBaseFxEnabled, NativeGimmick.UseNativeColorControls,
                name => Timeline.Get(name, layerTime)) : null)
        }).ToArray());
        var root = new JsonObject
        {
            ["reportSchemaVersion"] = 2,
            ["version"] = Paths.Version,
            ["build"] = Paths.BuildRevision,
            ["title"] = Title,
            ["objectName"] = Chart.ObjectName,
            ["assetsRoot"] = Paths.Assets,
            ["proxies"] = Chart.Proxies,
            ["proxyCountDeclared"] = Chart.ProxyCountDeclared,
            ["hasProxyTracks"] = HasProxyEvents,
            ["nativeApplicationProxyEnabled"] = NativeApplicationProxyEnabled,
            ["rendering"] = new JsonObject
            {
                ["width"] = Project.RenderWidth,
                ["height"] = Project.RenderWidth * 9 / 16,
                ["NoteAlignment"] = Project.NoteAlignment,
                ["VisualDelayMs"] = Project.VisualDelayMs,
                ["AudioDelayMs"] = Project.AudioDelayMs,
                ["PreviewVolume"] = Project.PreviewVolume
            },
            ["notes"] = AppJson.Node(Chart.Notes.GroupBy(n => n.Type).ToDictionary(g => g.Key.ToString(), g => g.Count())),
            ["songStats"] = songStats,
            ["windowMotion"] = new JsonObject
            {
                ["path"] = WindowMotion.Path ?? Project.WindowMotion,
                ["events"] = WindowMotion.Events?.Count ?? 0,
                ["windows"] = WindowMotion.Count,
                ["proxyBindings"] = WindowMotion.Bindings?.Count ?? 0,
                ["timeUnit"] = "seconds",
                ["editorTimelineUnit"] = "beats via the session BPM map",
                ["lifecyclePreview"] = "song-time motion only; no pause/FCAC/results simulation"
            },
            ["modCount"] = Chart.Mods.Count,
            ["perFrameCount"] = Chart.PerFrame.Count,
            ["duration"] = Duration,
            ["playbackDuration"] = Interludes!=null ? PresentationDuration : Playback.Duration(0, Duration),
            ["nativeStoryPauses"] = new JsonArray((Interludes?.Entries.Select(e=>(JsonNode)new JsonObject{
                ["songTime"]=e.SongTime,["presentationStart"]=e.PresentationStart,["duration"]=e.Duration,
                ["trigger"]=e.Schedule.Definition.Trigger}).ToArray())??[]),
            ["chartSpeed"] = time.HasValue ? Playback.RateAt(time.Value) : Playback.RateAt(0),
            ["playspeedSegments"] = AppJson.Node(Playback.Segments.ToArray()),
            ["musicControls"] = AppJson.Node(Playback.Controls.ToArray()),
            ["evaluationTime"] = JsonValue.Create(time),
            ["eventTimeline"] = new JsonObject
            {
                ["end"] = Timeline.End,
                ["audioEnd"] = JsonValue.Create(SongAudio?.Duration),
                ["presentationAudioEnd"] = JsonValue.Create(Audio?.Duration),
                ["pastAudioTail"] = pastAudioTail
            },
            ["sourceNoOps"] = AppJson.Node(Timeline.SourceNoOps.ToArray()),
            ["images"] = new JsonObject
            {
                ["manifest"] = Images.Path,
                ["count"] = Images.Items.Count,
                ["textures"] = Images.Files.Count
            },
            ["texts"] = new JsonObject
            {
                ["count"] = Texts.Tracks.Count,
                ["cues"] = Texts.Tracks.Sum(t => t.Cues.Count)
            },
            ["jacket"] = new JsonObject
            {
                ["mode"] = Jackets.Mode,
                ["path"] = Jackets.DefaultPath,
                ["count"] = Jackets.Count,
                ["available"] = AppJson.Node(Jackets.Files)
            },
            ["particles"] = new JsonObject
            {
                ["starsEnabled"] = Stars.Enabled,
                ["stars"] = Stars.Stars.Count,
                ["normal"] = Timeline.Particles.Count(p => !p.Burst),
                ["burst"] = Timeline.Particles.Count(p => p.Burst)
            },
            ["noteSkin"] = NoteSkinProfile.Report(Paths.Assets),
            ["nativeGimmick"] = new JsonObject
            {
                ["manifest"] = NativeGimmick.Manifest,
                ["definitionSource"] = NativeGimmick.DefinitionSource,
                ["definitionMigrations"] = AppJson.Node(NativeGimmick.DefinitionMigrations.ToArray()),
                ["nativeColorControls"] = NativeGimmick.UseNativeColorControls,
                ["declaredNativeColorControls"] = JsonValue.Create(NativeGimmick.Data?.UseNativeColorControls),
                ["background"] = NativeGimmick.Data?.Background != null,
                ["postModes"] = AppJson.Node(NativeGimmick.Data?.PostModes.Keys.ToArray()),
                ["objectName"] = NativeGimmick.Data?.ObjectName,
                ["definitionVersion"] = NativeGimmick.Data?.Version,
                ["components"] = AppJson.Node(NativeGimmick.Data?.Behaviors.Select(component=>component.Type).ToArray()),
                ["signalRoutes"] = AppJson.Node(NativeGimmick.Data?.Signals.Keys.ToArray()),
                ["sourceSongId"] = NativeGimmick.SongContext.SongId,
                ["sourceSongContext"] = NativeGimmick.SongContext.Source,
                ["sourceObscureInfo"] = NativeGimmick.SongContext.ObscureInfo,
                ["sourceDisablePause"] = NativeGimmick.SongContext.DisablePause,
                ["resources"] = AppJson.Node(NativeGimmick.Resources),
                ["issues"] = AppJson.Node(NativeGimmick.Issues),
                ["enabled"] = NativeGimmick.Data != null,
                ["resourceManifest"] = NativeGimmick.ResourceManifest,
                ["resourcePackLoaded"] = NativeGimmick.ResourcePackLoaded,
                ["room"] = NativeGimmick.ResourceRoom,
                ["sprites"] = AppJson.Node(NativeGimmick.Sprites.Keys.ToArray()),
                ["textures"] = AppJson.Node(NativeGimmick.TextureFiles),
                ["particles"] = Timeline.ObjectParticles.Count,
                ["declaredCompleteObject"] = NativeGimmick.Data?.CompleteObject ?? false,
                ["resourcesReady"] = NativeGimmick.ResourcesReady,
                ["completeObject"] = NativeGimmick.CompleteObject,
                ["declaredOnlyMods"] = AppJson.Node(NativeGimmick.DeclaredOnlyMods.ToArray()),
                ["starcrashersLateAtTime"] = NativeGimmick.HasBehavior("masked-starfield")
                    ? new JsonObject
                    {
                        ["greenleftMix8Bit"] = JsonValue.Create(time is { } mixTime
                            ? (double?)Math.Round(Math.Clamp(Timeline.Get("chorus_greenleft_overlay", mixTime), 0, 1) * 255) / 255 : null),
                        ["leftDustCallbacks"] = Chart.Mods.Count(e => e.Name == "chorus_satalli_alli_particles"),
                        ["rightDustCallbacks"] = Chart.Mods.Count(e => e.Name == "chorus_satalli_sat_particles"),
                        ["endingCallbacks"] = Chart.Mods.Count(e => e.Name == "ending_coroutine"),
                        ["endingVisible"] = JsonValue.Create(time is { } storyTime && storyTime >= 208.04 && storyTime < 222.58
                            && Chart.Mods.Any(e => e.Name == "ending_coroutine" && Timeline.Bpm.Time(e.Beat) <= storyTime))
                    } : null,
                ["roomLayersAtTime"] = new JsonArray((NativeGimmick.Data?.RoomLayers ?? [])
                    .OrderByDescending(l => l.Depth).Select(layer => (JsonNode?)new JsonObject
                    {
                        ["name"] = layer.Name,
                        ["sprite"] = layer.Sprite,
                        ["depth"] = layer.Depth,
                        ["alphaMod"] = layer.AlphaMod,
                        ["alpha"] = JsonValue.Create(time is { } layerTime
                            ? (double?)GimmickRoomLayerTimeline.Alpha(layer, Timeline, layerTime) : null),
                        ["x"] = JsonValue.Create(time is { } xTime
                            ? (double?)GimmickRoomLayerTimeline.X(layer, xTime) : null),
                        ["y"] = JsonValue.Create(time is { } yTime
                            ? (double?)GimmickRoomLayerTimeline.Y(layer, yTime) : null),
                        ["tileX"] = layer.TileHorizontal,
                        ["tileY"] = layer.TileVertical
                    }).ToArray()),
                ["callbacks"] = AppJson.Node(Timeline.Callbacks.Where(c => NativeGimmick.Data?.Callbacks.ContainsKey(c.Name) == true)
                    .GroupBy(c => c.Name).ToDictionary(g => g.Key, g => g.Count())),
                ["randomness"] = "Fixed seed for repeatable seeks and exports; not the random sequence of a recorded game run."
            },
            ["checker"] = new JsonObject
            {
                ["enabled"] = Checker.Enabled,
                ["available"] = Checker.Ready,
                ["refreshes"] = Checker.Updates.Count,
                ["frames"] = AppJson.Node(Checker.Frames)
            },
            ["metadata"] = AppJson.Node(Chart.Metadata),
            ["gameUi"] = new JsonObject
            {
                ["path"] = GameUi.Manifest,
                ["available"] = GameUi.Ready,
                ["enabled"] = Project.GameUiEnabled,
                ["font"] = Project.GameUiFont,
                ["song"] = AppJson.Node(Song),
                ["scoring"] = "No input judgement; score and EX start at zero. Decorative score images authored in VSP remain chart-controlled."
            },
            ["fx"] = new JsonObject
            {
                ["path"] = Fx.Path,
                ["room"] = Fx.Room,
                ["source"] = Fx.Source,
                ["autoFallback"] = Fx.AutoFallback,
                ["association"] = Fx.RoomAssociation,
                ["preset"] = Project.RoomPreset,
                ["dynamicDefinitions"] = Fx.DynamicDefinitions,
                ["fxRed"] = JsonValue.Create(time is { } redTime ? (double?)Timeline.Get("fx_red", redTime) : null),
                ["layers"] = fxLayers
            },
            ["profile"] = NativeGimmick.Data?.ObjectName ?? "core",
            ["bpm"] = AppJson.Node(Timeline.Bpm.Segments.ToArray()),
            ["diagnostics"] = AppJson.Node(Chart.Diagnostics.ToArray())
        };
        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }
}
