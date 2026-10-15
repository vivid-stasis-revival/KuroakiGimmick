using System.Diagnostics.CodeAnalysis;

namespace KuroakiGimmick.Core;

/// <summary>
/// 在进入时间轴和 GPU 之前验证配置。上限不仅防止坏资源，也限制配置驱动循环的复杂度。
/// 验证不认识歌曲；所有约束来自公共 schema 或对象 manifest 中声明的资源契约。
/// </summary>
public static class GimmickValidation
{
    /// <summary>
    /// 加载时一次性校验结构、数值范围和引用图；任一项不满足立即抛 InvalidDataException，不做静默裁剪。
    /// 必须在建立时间轴和创建 GPU 资源之前调用，否则坏配置会在逐帧求值阶段才暴露。
    /// </summary>
    public static void Validate(GimmickDefinition definition)
    {
        Require(definition.Behaviors!=null && definition.Behaviors.Count<=32 && definition.Behaviors.All(c=>c!=null)
            && definition.Behaviors.Select(c=>c.Type).Distinct(StringComparer.Ordinal).Count()==definition.Behaviors.Count,"Invalid or duplicate runtime components.");
        foreach(var component in definition.Behaviors)
        {
            Require(NativeGimmickCapabilities.Types.Contains(component.Type),"Unknown runtime component: "+component.Type);
            Require(component.Mods!=null && component.Mods.Count<=64 && component.Mods.All(p=>NativeGimmickCapabilities.Roles(component.Type).Contains(p.Key)
                && !string.IsNullOrWhiteSpace(p.Value)),"Unknown component mod role: "+component.Type);
            Require(component.Sprites!=null && component.Sprites.Count<=64 && component.Sprites.All(p=>!string.IsNullOrWhiteSpace(p.Key)
                && definition.Sprites.ContainsKey(p.Value)),"Invalid component sprite binding: "+component.Type);
            Require(component.SpriteLists!=null && component.SpriteLists.Count<=16 && component.SpriteLists.All(p=>!string.IsNullOrWhiteSpace(p.Key)
                && p.Value!=null && p.Value.Length is >0 and <=256 && p.Value.All(definition.Sprites.ContainsKey)),"Invalid component sprite lists.");
            Require(component.Parameters!=null && component.Parameters.Count<=64 && component.Parameters.All(p=>!string.IsNullOrWhiteSpace(p.Key)
                && (p.Value.ValueKind is System.Text.Json.JsonValueKind.True or System.Text.Json.JsonValueKind.False or System.Text.Json.JsonValueKind.String
                    || p.Value.ValueKind==System.Text.Json.JsonValueKind.Number && p.Value.TryGetDouble(out var n)&&double.IsFinite(n))),"Invalid component parameters.");
            foreach(var (name,value) in component.Parameters)
                Require(NativeGimmickCapabilities.ParameterType(component.Type,name) switch
                {
                    "number"=>value.ValueKind==System.Text.Json.JsonValueKind.Number,
                    "string"=>value.ValueKind==System.Text.Json.JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString()),
                    "bool"=>value.ValueKind is System.Text.Json.JsonValueKind.True or System.Text.Json.JsonValueKind.False,
                    _=>false
                },"Unknown or incorrectly typed component parameter: "+component.Type+"/"+name);
            if(component.Type=="multiply-jacket")Require(component.Mods.ContainsKey("frame")
                &&(component.Sprites.ContainsKey("atlas")||component.SpriteLists.ContainsKey("variants"))
                &&component.Number("width",320)>0&&component.Number("height",180)>0,"Invalid multiply-jacket configuration.");
        }
        Require(definition.Signals!=null && definition.Signals.Count<=128 && definition.Signals.All(p=>!string.IsNullOrWhiteSpace(p.Key)
            && p.Value!=null && !string.IsNullOrWhiteSpace(p.Value.Signal) && p.Value.Operation is "signal" or "random-write"
            && Finite(p.Value.Lifetime,p.Value.RandomScale) && p.Value.Lifetime is >=0 and <=3600 && p.Value.RandomScale is >=0 and <=1000000
            && (p.Value.Operation!="random-write"||!string.IsNullOrWhiteSpace(p.Value.Target))
            && (p.Value.AbsoluteEnd==null||double.IsFinite(p.Value.AbsoluteEnd.Value)&&p.Value.AbsoluteEnd>=0)
            && (p.Value.MinimumExclusive==null||double.IsFinite(p.Value.MinimumExclusive.Value))),"Invalid signal routes.");
        Require(definition.ContextRules!=null && definition.ContextRules.Count<=32 && definition.ContextRules.All(r=>r!=null&&r.All!=null&&r.All.Count is >0 and <=16
            && r.All.All(c=>c!=null&&!string.IsNullOrWhiteSpace(c.Key)&&c.Value.ValueKind is not (System.Text.Json.JsonValueKind.Undefined or System.Text.Json.JsonValueKind.Object or System.Text.Json.JsonValueKind.Array))
            && r.Patch.ValueKind==System.Text.Json.JsonValueKind.Object && r.Patch.GetRawText().Length<=1048576),"Invalid context rules.");
        Require(definition.SkinRules!=null && definition.SkinRules.Count<=32 && definition.SkinRules.All(r=>r!=null&&!string.IsNullOrWhiteSpace(r.Skin)
            && ModName(r.Mod)&&double.IsFinite(r.Threshold)&&(r.Start==null||double.IsFinite(r.Start.Value))&&(r.End==null||double.IsFinite(r.End.Value))
            && (r.Start==null||r.End==null||r.Start<=r.End)),"Invalid note-skin rules.");
        if(definition.CompositeLifecycle is { } lifecycle)ValidateLifecycle(lifecycle);
        Require(definition.FunctionPrograms!=null && definition.FunctionPrograms.Count<=32 && definition.FunctionPrograms.All(p=>!string.IsNullOrWhiteSpace(p.Key)
            && p.Value!=null && NativeFunctionTimeline.Kernels.Contains(p.Value.Kernel) && p.Value.Inputs!=null && p.Value.Inputs.Length<=64
            && p.Value.Inputs.All(s=>!string.IsNullOrWhiteSpace(s)) && p.Value.Parameters!=null && p.Value.Parameters.Count<=64
            && p.Value.Parameters.All(v=>!string.IsNullOrWhiteSpace(v.Key)&&double.IsFinite(v.Value)) && p.Value.Windows!=null&&p.Value.Windows.Count<=32
            && p.Value.Windows.All(w=>w!=null&&Finite(w.Start,w.End)&&w.Start<w.End&&w.Outputs!=null&&w.Outputs.Length is >0 and <=32
                && w.Outputs.All(n=>!string.IsNullOrWhiteSpace(n)))),"Invalid per-frame programs.");
        Require(definition.PlayfieldLayerDepth==null || double.IsFinite(definition.PlayfieldLayerDepth.Value),"Invalid playfield layer depth.");
        Require(definition.UnavailableCallbacks!=null && definition.UnavailableCallbacks.Count<=128 && definition.UnavailableCallbacks.All(p=>!string.IsNullOrWhiteSpace(p.Key)
            && !string.IsNullOrWhiteSpace(p.Value)),"Invalid callback dependency diagnostics.");
        Require(definition.ParameterObjectPath != null, "ParameterObjectPath must not be null.");
        Require(definition.HudAfterPostWhenPositive == null || !string.IsNullOrWhiteSpace(definition.HudAfterPostWhenPositive),
            "Invalid dynamic source HUD gate.");
        Require(ModName(definition.SourceRoom), "Invalid source room identity.");
        Require(definition.SourceNoOps != null && definition.SourceNoOps.Count <= 128 && definition.SourceNoOps.All(p => !string.IsNullOrWhiteSpace(p.Key) && !string.IsNullOrWhiteSpace(p.Value)), "Invalid documented source no-ops.");
        if (definition.Sequence is { } sequence)
        {
            Require(sequence.AutomaticTriggers != null && sequence.AutomaticTriggers.Count <= 64 &&
                sequence.AutomaticTriggers.All(p => !string.IsNullOrWhiteSpace(p.Key) && double.IsFinite(p.Value) && p.Value >= 0), "Invalid automatic source triggers.");
            Require(sequence.JacketEndTime == null || double.IsFinite(sequence.JacketEndTime.Value) && sequence.JacketEndTime >= 0,
                "Invalid native jacket destruction time.");
            Require(definition.Sprites != null && sequence.Sprites != null && sequence.Mods != null && sequence.BackgroundLayers != null,
                "Invalid native sequence roles.");
            Require(sequence.Sprites.Count <= 32 && sequence.Mods.Count <= 32 && sequence.BackgroundLayers.Count <= 8,
                "Native sequence role limits exceeded.");
            Require(sequence.Sprites.All(p => !string.IsNullOrWhiteSpace(p.Key) && !string.IsNullOrWhiteSpace(p.Value) && definition.Sprites.ContainsKey(p.Value)) &&
                sequence.Mods.All(p => !string.IsNullOrWhiteSpace(p.Key) && !string.IsNullOrWhiteSpace(p.Value)) &&
                sequence.BackgroundLayers.All(p => !string.IsNullOrWhiteSpace(p.Key) && !string.IsNullOrWhiteSpace(p.Value)), "Undeclared sequence sprite or mod.");
            Require(sequence.Story != null && sequence.Story.Count <= 1024 && sequence.Story.All(c => c != null && double.IsFinite(c.Time) && c.Time >= 0 &&
                double.IsFinite(c.Speed) && c.Speed > 0 && !string.IsNullOrWhiteSpace(c.Trigger) && c.Speaker != null && c.Text is { Length: <= 32768 }), "Invalid native story cues.");
            Require(sequence.AudioCues != null && sequence.AudioCues.Count <= 32 && sequence.AudioCues.All(c => c != null &&
                double.IsFinite(c.Time) && c.Time is >= 0 and <= 3600 && double.IsFinite(c.Gain) && c.Gain is >= 0 and <= 4 &&
                !string.IsNullOrWhiteSpace(c.Trigger) && !string.IsNullOrWhiteSpace(c.Path) && !Path.IsPathRooted(c.Path) &&
                !c.Path.Contains('\\') && !c.Path.Contains(':') && c.Path.Split('/').All(p => p is not ("" or "." or ".."))), "Invalid native audio cue.");
            Require(sequence.StoryWindows != null && sequence.StoryWindows.Count <= 128 && sequence.StoryWindows.All(w =>
                w != null && !string.IsNullOrWhiteSpace(w.Trigger) && double.IsFinite(w.Start) && w.Start >= 0 &&
                Finite(w.ExitFrom, w.ExitSeconds) && w.ExitSeconds > 0 &&
                (w.Clear == null || double.IsFinite(w.Clear.Value) && w.Clear >= w.Start) &&
                (w.Destroy == null || double.IsFinite(w.Destroy.Value) && w.Destroy >= (w.Clear ?? w.Start))), "Invalid native story windows.");
            Require(sequence.MusicFades != null && sequence.MusicFades.Count <= 32 && sequence.MusicFades.All(f => f != null &&
                Finite(f.Time, f.Duration, f.From, f.To) && f.Time is >= 0 and <= 3600 && f.Duration is >= 0 and <= 3600 &&
                f.From is >= 0 and <= 4 && f.To is >= 0 and <= 4 && !string.IsNullOrWhiteSpace(f.Trigger)), "Invalid native music fade.");
            Require(sequence.SongSeeks != null && sequence.SongSeeks.Count <= 32 && sequence.SongSeeks.All(s => s != null &&
                !string.IsNullOrWhiteSpace(s.Trigger) && Finite(s.Time, s.FadeInSeconds) && s.Time is >= 0 and <= 3600 &&
                s.FadeInSeconds is >= 0 and <= 3600), "Invalid native song seek.");
            Require(sequence.EndFadeTime == null || double.IsFinite(sequence.EndFadeTime.Value) && sequence.EndFadeTime >= 0 &&
                double.IsFinite(sequence.EndFadeDuration) && sequence.EndFadeDuration > 0 && !string.IsNullOrWhiteSpace(sequence.EndFadeTrigger), "Invalid sequence end fade.");
        }
        Require(definition.Version is 1 or 2 && !string.IsNullOrWhiteSpace(definition.ObjectName), "Invalid object definition version/name.");
        // 扩展表按位置编码，旧格式允许不同 ID 对应同名 mod；禁止去重，否则后续 ID 全部错位。
        Require(definition.ExtraMods != null && definition.ExtraMods.Length <= 127
            && definition.ExtraMods.All(name => !string.IsNullOrWhiteSpace(name)), "At most 127 binary mod IDs are allowed.");
        Require(definition.ProfileAliases != null && definition.ProfileAliases.Length <= 32 && definition.PreviewMods != null
            && definition.PreviewMods.Length <= 16 && definition.ProfileAliases.All(name => !string.IsNullOrWhiteSpace(name))
            && definition.PreviewMods.All(name => !string.IsNullOrWhiteSpace(name)), "Invalid profile aliases or preview mods.");
        Require(definition.Defaults != null && definition.Defaults.Count <= 512 && definition.Defaults.All(p => !string.IsNullOrWhiteSpace(p.Key) && double.IsFinite(p.Value)),
            "Invalid mod defaults.");
        Require(definition.ModAliases != null && definition.ModAliases.Count <= 128
            && definition.ModAliases.All(pair => !string.IsNullOrWhiteSpace(pair.Key) && !string.IsNullOrWhiteSpace(pair.Value)),
            "Invalid mod aliases.");
        Require(definition.Sprites != null && definition.Sprites.Count <= 64 && definition.RequiredSprites != null
            && definition.RequiredSprites.Count <= 64 && definition.Textures != null && definition.Textures.Count <= 16
            && definition.Shaders != null && definition.Shaders.Count <= 16 &&
            definition.Sprites.Keys.Concat(definition.RequiredSprites.Keys).Concat(definition.Textures.Keys).All(n => !string.IsNullOrWhiteSpace(n)) &&
            definition.Shaders.All(p => !string.IsNullOrWhiteSpace(p.Key) && !string.IsNullOrWhiteSpace(p.Value)), "Invalid resource dictionary/count.");
        Require(definition.Callbacks != null && definition.Callbacks.Count <= 128 && definition.Callbacks.Values.All(items => items != null
            && items.Count is >= 1 and <= 64) && definition.Callbacks.Keys.All(n => !string.IsNullOrWhiteSpace(n)) &&
            definition.Overlays != null && definition.Overlays.Count <= 64, "Invalid callbacks/overlays.");
        Require(definition.SideCallbacks != null && definition.SideCallbacks.Count <= 64 &&
            definition.SideCallbacks.All(p => !string.IsNullOrWhiteSpace(p.Key) && p.Value is "unravel" or "astellion" or "apocalypse") &&
            !definition.SideCallbacks.Keys.Any(definition.Callbacks.ContainsKey), "Invalid or ambiguous side callback.");
        Require(definition.CallbackCancels != null && definition.CallbackCancels.Count <= 64 &&
            definition.CallbackCancels.All(p => !string.IsNullOrWhiteSpace(p.Key) && !definition.Callbacks.ContainsKey(p.Key) &&
                !definition.SideCallbacks.ContainsKey(p.Key) && p.Value != null && p.Value.Length is >= 1 and <= 64 &&
                p.Value.All(name => !string.IsNullOrWhiteSpace(name) && definition.Callbacks.ContainsKey(name))), "Invalid callback cancellation.");
        Require(definition.PositiveSideCallbacks != null && definition.PositiveSideCallbacks.Length <= 64 &&
            definition.PositiveSideCallbacks.All(name => !string.IsNullOrWhiteSpace(name) && definition.SideCallbacks.ContainsKey(name)), "Positive side callback must name a declared side callback.");
        Require(definition.CallbackConstraints != null && definition.CallbackConstraints.Count <= 128 && definition.CallbackFades != null
            && definition.CallbackFades.Count <= 128 && definition.PerFrameBindings != null && definition.PerFrameBindings.Count <= 32,
            "Invalid timeline bindings.");
        Require(definition.PostModes != null && definition.PostModes.Count <= 16 && definition.PostPasses != null && definition.PostPasses.Count <= 16 && definition.CheckerMode is >= 0 and <= 2,
            "Invalid shader modes/checkerboard mode.");
        ValidateBpm(definition.InitialBpm);
        Require(definition.TextOverlays != null && definition.TextOverlays.Count <= 32, "Invalid text overlay count.");
        Require(definition.SourceTextTables != null && definition.SourceTextTables.Count <= 32 &&
            definition.SourceTextTables.All(p => !string.IsNullOrWhiteSpace(p.Key) && p.Value != null && p.Value.Length <= 2048 &&
                p.Value.All(text => text != null && text.Length <= 32768) && p.Value.Sum(text => (long)text.Length) <= 1048576),
            "Invalid native source text tables.");
        foreach (var text in definition.TextOverlays)
            Require(text != null && text.Frames != null && text.Frames.Length is > 0 and <= 2048
                && text.Frames.All(s => s != null && s.Length <= 32768) && text.Frames.Sum(s => (long)s.Length) <= 1048576
                && ModName(text.IndexMod) && ModName(text.AlphaMod) && Finite(text.X,text.Y,text.Alpha,text.Scale)
                && text.Scale is > 0 and <= 100 && (text.BottomColorBgr==null || text.BottomColorBgr<=0xffffff)
                && Math.Abs(text.X) <= 100000 && Math.Abs(text.Y) <= 100000 && text.Alpha >= 0 && text.Alpha <= 1
                && text.ColorBgr <= 0xffffff && text.OutlineColorBgr <= 0xffffff, "Invalid source text overlay.");
        ValidateBpm(definition.FixedBpm);
        Require(definition.OffsetOverrideMs==null || double.IsFinite(definition.OffsetOverrideMs.Value)
            && Math.Abs(definition.OffsetOverrideMs.Value)<=3600000,"Invalid native chart offset.");
        Require(definition.SceneComposite is null or "desktop" or "filter-chain",
            "Unknown native scene compositor.");
        Require(definition.CompositeSource is "application" or "final","Unknown native composite source.");
        Require(definition.BpmOverride != null && definition.BpmOverride.Count <= 4096,
            "Invalid native BPM override count.");
        double previousBeat = 0;
        foreach (var point in definition.BpmOverride)
        {
            Require(point != null && double.IsFinite(point.Beat) && point.Beat >= previousBeat,
                "Native BPM points must be finite and ordered.");
            ValidateBpm(point.Bpm);
            previousBeat = point.Beat;
        }
        Require(definition.BpmOverride.Count == 0 || definition.BpmOverride[0].Beat == 0,
            "Native BPM override must start at zero.");
        bool HasSprite(string name) => !string.IsNullOrWhiteSpace(name) && (definition.Sprites.ContainsKey(name)
            || definition.RequiredSprites.ContainsKey(name));
        if(definition.Desktop is { } desktop)
            Require(HasSprite(desktop.Desktop)&&HasSprite(desktop.WindowFrame)
                && (desktop.Cameo==null||HasSprite(desktop.Cameo)) && (desktop.Static==null||HasSprite(desktop.Static))
                && desktop.WindowTitles!=null && desktop.WindowTitles.Length is >0 and <=256
                && desktop.WindowTitles.All(t=>t!=null && t.Length<=2048),"Invalid native desktop resources.");
        Require(definition.CompositePasses != null && definition.CompositePasses.Count <= 16, "Invalid source composite pass count.");
        if (definition.Console is { } stopmotion)
        {
            Require(stopmotion.AreaBackgrounds != null && stopmotion.AreaBackgrounds.Length <= 64 && stopmotion.AreaBackgrounds.All(HasSprite) &&
                stopmotion.AreaFrames != null && stopmotion.AreaFrames.Length == stopmotion.AreaBackgrounds.Length &&
                stopmotion.Commands != null && stopmotion.Commands.Length <= 4096 && stopmotion.Commands.All(s => s is { Length: <= 32768 }) &&
                stopmotion.BarTexts != null && stopmotion.BarTexts.Length <= 4 && stopmotion.BarTexts.All(s => s is { Length: <= 32768 }) &&
                stopmotion.BarColors != null && stopmotion.BarColors.Length <= 4 && stopmotion.BarColors.All(c => c <= 0xffffff), "Invalid source memory-merge tables.");
            for (int i = 0; i < stopmotion.AreaFrames.Length; i++)
            {
                string sprite = stopmotion.AreaBackgrounds[i];
                int frames = definition.Sprites.TryGetValue(sprite, out var item) ? item?.Frames?.Count ?? 0 : definition.RequiredSprites[sprite].Frames;
                Require(stopmotion.AreaFrames[i] >= 0 && stopmotion.AreaFrames[i] < frames, "Invalid source background-bank frame.");
            }
        }
        ValidateInterludes(definition.Interludes, HasSprite);
        if (definition.Sequence is { } sceneSequence)
        {
            Require(sceneSequence.SceneActions != null && sceneSequence.SceneActions.Count <= 512 &&
                sceneSequence.SceneActions.All(x => x != null && double.IsFinite(x.Time) && x.Time >= 0 && !string.IsNullOrWhiteSpace(x.Trigger)),
                "Invalid native scene action time.");
            ValidateInterludes([new() { Trigger = "native-scene", Steps = sceneSequence.SceneActions.Select(x =>
                new NativeInterludeStep { Duration = 0, Actions = [x.Action] }).ToList() }], HasSprite);
            Require(sceneSequence.ModWrites != null && sceneSequence.ModWrites.Count <= 128 && sceneSequence.ModWrites.All(x =>
                x != null && double.IsFinite(x.Time) && x.Time >= 0 && double.IsFinite(x.Value) && !string.IsNullOrWhiteSpace(x.Mod) &&
                !string.IsNullOrWhiteSpace(x.Trigger) && x.Condition is null or "plaudite_true_ending" or "!plaudite_true_ending"),
                "Invalid native source mod write.");
        }
        Require(definition.DialogueTable != null && definition.DialogueTable.Count <= 1024 &&
            definition.DialogueTable.All(line => line != null && line.Sender is { Length: <= 256 } && line.Text is { Length: <= 32768 }),
            "Invalid native dialogue table.");
        Require(definition.RoomLayers != null && definition.RoomLayers.Count <= 32 &&
            definition.MaskLayers != null && definition.MaskLayers.Count <= 32, "Invalid room or mask layer count.");
        foreach (var roomLayer in definition.RoomLayers)
        {
            Require(roomLayer!=null&&roomLayer.Drawings!=null&&roomLayer.Drawings.Count<=64&&ModName(roomLayer.FrameSelectionEvent),"Invalid room component drawings or frame-selection event.");
            foreach(var drawing in roomLayer.Drawings)ValidateDrawing(drawing,HasSprite);
            Require(roomLayer != null && (!roomLayer.FilterOnly || roomLayer.Sprite == null && roomLayer.Effect != null),
                "Filter-only room layers require an effect and no sprite.");
            Require(roomLayer != null && Finite(roomLayer.Alpha, roomLayer.ScrollY, roomLayer.ScrollX, roomLayer.OffsetX, roomLayer.OffsetY, roomLayer.FramesPerSecond) &&
                roomLayer.Alpha >= 0 && roomLayer.Alpha <= 1 && roomLayer.ColorBgr <= 0xFFFFFF && roomLayer.FramesPerSecond is >= 0 and <= 1000 &&
                ModName(roomLayer.AlphaMod) && ModName(roomLayer.VisibleMod) && ModName(roomLayer.FrameMod) && ModName(roomLayer.FrameSpeedMod) &&
                (roomLayer.Sprite == null || HasSprite(roomLayer.Sprite)), "Invalid native room layer.");
            ValidateBindings(roomLayer.Bindings, "x", "y", "alpha", "frame");
        }
        foreach (var maskLayer in definition.MaskLayers)
            ValidateDrawing(maskLayer, HasSprite);
        foreach (var requirement in definition.RequiredSprites.Values)
        {
            Require(requirement != null && requirement.Width is >= 1 and <= 4096 && requirement.Height is >= 1 and <= 4096
                && requirement.Frames is >= 1 and <= 64, "Invalid sprite requirement.");
        }
        foreach (var drawing in definition.Callbacks.Values.SelectMany(items => items).Concat(definition.Overlays))
        {
            ValidateDrawing(drawing, HasSprite);
        }
        if (definition.Background != null)
        {
            ValidateDrawing(definition.Background.Drawing, HasSprite);
        }
        Require(definition.CallbackLifetimes != null && definition.CallbackLifetimes.Count <= 128, "Invalid callback lifetimes.");
        // 时间轴尾部占用（秒）必须覆盖该回调下最长的可见绘制寿命；两者单位相同但含义不同，不能互相替代。
        foreach (var (name, lifetime) in definition.CallbackLifetimes)
        {
            bool side = definition.SideCallbacks.ContainsKey(name);
            Require((side || definition.Callbacks.ContainsKey(name)) && double.IsFinite(lifetime) && lifetime is > 0 and <= 60
                && lifetime >= (side ? 1 : definition.Callbacks[name].Max(drawing => drawing.Lifetime)),
                "Callback lifetime must cover its drawings: " + name);
        }
        foreach (var (name, constraint) in definition.CallbackConstraints)
        {
            Require(definition.Callbacks.ContainsKey(name) && constraint != null && Finite(constraint.Minimum, constraint.Maximum)
                && constraint.Minimum <= constraint.Maximum, "Invalid callback value constraint: " + name);
        }
        foreach (var (name, fade) in definition.CallbackFades)
        {
            Require(!string.IsNullOrWhiteSpace(name) && fade != null && definition.Callbacks.ContainsKey(fade.Callback) && Finite(fade.Duration,
                fade.From, fade.To) && fade.Duration is >= 0 and <= 3600 && Easings.IsKnown(fade.Ease), "Invalid callback fade: " + name);
        }
        foreach (var (name, bindings) in definition.PerFrameBindings)
        {
            Require(!string.IsNullOrWhiteSpace(name) && bindings != null && bindings.Count is >= 1 and <= 32, "Invalid per-frame binding: " + name);
            foreach (var (mod, scalar) in bindings)
            {
                Require(!string.IsNullOrWhiteSpace(mod), "Missing per-frame output mod.");
                ValidateScalar(scalar);
                // 资源参数可能整包缺失，而逐帧求值没有按组件停用的退路；只有 shader uniform 能在加载时校验并禁用该 pass。
                Require(scalar.Parameter == null, "Per-frame bindings cannot depend on optional resource parameters.");
            }
        }
        Require(definition.ValueBindings != null && definition.ValueBindings.Count <= 128,"Invalid value bindings.");
        foreach(var (name,scalar) in definition.ValueBindings)
        {
            Require(!string.IsNullOrWhiteSpace(name),"Missing value binding name.");
            ValidateScalar(scalar);
            Require(scalar.Parameter==null,"Value bindings cannot depend on optional resource parameters.");
        }
        ValidateReferenceGraph(definition);
        foreach (var texture in definition.Textures.Values)
        {
            Require(texture != null && texture.Candidates != null && texture.Candidates.Count <= 16 && texture.Aliases != null
                && texture.Aliases.Count <= 32, "Invalid texture source.");
            foreach (var candidates in texture.Aliases.Values.Append(texture.Candidates))
            {
                Require(candidates != null && candidates.Count <= 16, "Invalid texture candidate list.");
                foreach (var candidate in candidates)
                {
                    Require(candidate != null && candidate.Scope is "pack" or "definition" or "shared"
                        && !string.IsNullOrWhiteSpace(candidate.Path), "Invalid texture candidate.");
                }
            }
        }
        foreach (var (_, mode) in ShaderModes(definition))
        {
            Require(mode != null && !string.IsNullOrWhiteSpace(mode.Shader) && definition.Shaders.ContainsKey(mode.Shader) && mode.Uniforms != null && mode.Uniforms.Count <= 64
                && mode.Samplers != null && mode.Samplers.Count <= 8 && mode.LinearSamplers != null
                && mode.LinearSamplers.All(n => n != null && mode.Samplers.ContainsKey(n)) &&
                mode.ClampSamplers != null && mode.ClampSamplers.All(n => n != null && mode.Samplers.ContainsKey(n)) &&
                mode.Uniforms.Keys.Concat(mode.Samplers.Keys).All(n => !string.IsNullOrWhiteSpace(n)), "Invalid shader/sampler binding.");
            foreach (string sampler in mode.Samplers.Values)
            {
                Require(HasSprite(sampler) || definition.Textures.ContainsKey(sampler), "Undeclared shader sampler: " + sampler);
            }
            foreach (var values in mode.Uniforms.Values)
            {
                Require(values != null && values.Length is >= 1 and <= 4, "Uniforms require 1..4 scalar components.");
                foreach (var scalar in values)
                {
                    ValidateScalar(scalar);
                }
            }
        }
        Require(definition.PostModes.Count == 0 || !string.IsNullOrWhiteSpace(definition.PostModeMod), "Missing post shader selector mod.");
        if (definition.ParticleEmitter is { } emitter)
        {
            Require(HasSprite(emitter.Sprite) && !string.IsNullOrWhiteSpace(emitter.IntervalMod)
                && emitter.ParticlesPerEmission is >= 1 and <= 64 && Finite(emitter.Width, emitter.Height, emitter.Speed, emitter.Lifetime,
                emitter.Margin) && emitter.Width is > 0 and <= 4096 && emitter.Height is > 0 and <= 4096 && emitter.Speed is >= 0 and <= 1000
                && emitter.Lifetime is > 0 and <= 60 && emitter.Margin is >= 0 and <= 4096, "Invalid particle emitter.");
        }
    }

    public static void ValidateLifecycle(GimmickLifecycleDefinition lifecycle)
    {
        Require(lifecycle.Create!=null&&lifecycle.Destroy!=null&&lifecycle.Create.Length<=64&&lifecycle.Destroy.Length<=64
            && lifecycle.Create.Concat(lifecycle.Destroy).All(s=>!string.IsNullOrWhiteSpace(s)),"Invalid lifecycle events.");
    }

    /// <summary>枚举所有 shader pass 及其组件名；组件名同时是逐组件错误记录的键，改名会改变诊断标识。</summary>
    public static IEnumerable<(string Component, GimmickShaderMode Mode)> ShaderModes(GimmickDefinition definition)
    {
        foreach (var (index, mode) in definition.PostModes)
        {
            yield return ("post/" + index, mode);
        }
        for (int index = 0; index < definition.PostPasses.Count; index++)
            yield return ("post-pass/" + index, definition.PostPasses[index]);
        for (int index = 0; index < definition.CompositePasses.Count; index++)
            yield return ("composite-pass/" + index, definition.CompositePasses[index]);
        for (int index = 0; index < definition.RoomLayers.Count; index++)
            if (definition.RoomLayers[index].Effect is { } effect) yield return ("room-layer/" + index, effect);
        if (definition.Background?.Effect is { } background)
        {
            yield return ("background/" + background.Shader, background);
        }
        if(definition.Desktop?.WindowShader is { } windowShader)
            yield return ("desktop-window/"+windowShader.Shader,windowShader);
        if (definition.Sequence?.CgEffect is { } cg)
            yield return ("sequence-cg/" + cg.Shader, cg);
    }

    /// <summary>
    /// 校验 manifest 声明的精灵几何：尺寸 1..4096、原点必须落在尺寸之内、帧数 1..64、Scope 限 pack/definition/shared。
    /// 这里只看声明值；解码后的纹理像素尺寸由加载阶段另行比对。
    /// </summary>
    public static void ValidateSprite(GimmickSprite? sprite)
    {
        Require(sprite != null && sprite.Width is >= 1 and <= 4096 && sprite.Height is >= 1 and <= 4096 && sprite.Frames != null
            && sprite.Frames.Count is >= 1 and <= 256 && sprite.OriginX >= 0 && sprite.OriginX <= sprite.Width && sprite.OriginY >= 0
            && sprite.OriginY <= sprite.Height && sprite.Frames.All(n => !string.IsNullOrWhiteSpace(n)) && sprite.Scope is null or "pack" or "definition" or "shared",
            "Invalid sprite dimensions, origin, frames or scope.");
    }

    private static void ValidateDrawing(GimmickDrawing? drawing, Func<string, bool> hasSprite)
    {
        Require(drawing != null && hasSprite(drawing.Sprite) && GimmickStages.All.Contains(drawing.Stage) && drawing.Tweens != null
            && drawing.Tweens.Count <= 32 && drawing.SuppressRanges != null && drawing.SuppressRanges.Count <= 64 && Finite(drawing.X, drawing.Y,
            drawing.ScaleX, drawing.ScaleY, drawing.Alpha, drawing.Angle, drawing.VelocityX, drawing.VelocityY, drawing.FramesPerSecond,
            drawing.Lifetime) && drawing.Lifetime is > 0 and <= 60 && drawing.FramesPerSecond is >= 0 and <= 1000 && drawing.Frame >= 0
            && (drawing.EventValue == null || double.IsFinite(drawing.EventValue.Value))
            && drawing.ColorBgr <= 0xFFFFFF && ModName(drawing.AlphaMod) && ModName(drawing.FrameMod), "Invalid drawing sprite, stage or transform.");
        Require(Finite(drawing.MinimumAlpha,drawing.MinimumAbsScale) && drawing.MinimumAlpha>=0 && drawing.MinimumAlpha<=1
            && drawing.MinimumAbsScale>=0,"Invalid drawing visibility thresholds.");
        ValidateBindings(drawing.Bindings, "x", "y", "scaleX", "scaleY", "angle", "alpha", "frame", "color");
        if(drawing.Lifecycle is { } lifecycle)ValidateLifecycle(lifecycle);
        if(drawing.HsvTint is { } tint)
        {
            ValidateScalar(tint.Hue);
            Require(Finite(tint.Saturation,tint.Brightness)&&tint.Saturation is >=0 and <=1&&tint.Brightness is >=0 and <=1,"Invalid HSV tint.");
        }
        foreach (var range in drawing.SuppressRanges)
        {
            Require(range != null && Finite(range.Start, range.End) && range.Start <= range.End, "Invalid callback suppression interval.");
        }
        foreach (var tween in drawing.Tweens)
        {
            Require(tween != null && tween.Property is "x" or "y" or "scaleX" or "scaleY" or "alpha" or "angle" && Finite(tween.Delay,
                tween.Duration, tween.From, tween.To) && tween.Delay >= 0 && tween.Duration >= 0 && tween.Delay + tween.Duration <= 60
                && Easings.IsKnown(tween.Ease), "Invalid drawing tween.");
        }
        // OrderBy 是稳定排序：Delay 相同的赋值保持作者书写顺序。
        drawing.Tweens = drawing.Tweens.OrderBy(tween => tween.Delay).ToList();
        if (drawing.Random is { } random)
        {
            Require(Finite(random.Width, random.Height) && random.Width is >= 0 and <= 4096 && random.Height is >= 0 and <= 4096,
                "Invalid random placement range.");
        }
    }

    private static void ValidateScalar(GimmickScalar? scalar)
    {
        Require(scalar != null && scalar.MultiplyMods != null && scalar.MultiplyMods.Length <= 8
            && scalar.MultiplyMods.All(name=>!string.IsNullOrWhiteSpace(name))
            && (scalar.Minimum==null || double.IsFinite(scalar.Minimum.Value))
            && (scalar.Maximum==null || double.IsFinite(scalar.Maximum.Value))
            && (scalar.Minimum==null || scalar.Maximum==null || scalar.Minimum<=scalar.Maximum),"Invalid scalar products or bounds.");
        Require(scalar != null && scalar.Mods != null && scalar.Mods.Length <= 8 && scalar.Mods.All(mod => !string.IsNullOrWhiteSpace(mod))
            && scalar.Wave is null or "sin" or "cos" && ModName(scalar.MultiplyMod) && ModName(scalar.Parameter) && Finite(scalar.Constant, scalar.TimeScale, scalar.TimeOffset, scalar.BeatScale, scalar.Scale)
            && Math.Abs(scalar.TimeOffset)<=60
            && scalar.ParameterIndex is >= -1 and <= 3 && (scalar.ParameterMinimumExclusive == null
            || double.IsFinite(scalar.ParameterMinimumExclusive.Value)) && (scalar.Wrap == null || double.IsFinite(scalar.Wrap.Value)
            && scalar.Wrap > 0), "Invalid scalar binding.");
        Require(scalar.DifficultyScale == null || scalar.DifficultyScale.Count <= 8 && scalar.DifficultyScale.All(p => !string.IsNullOrWhiteSpace(p.Key) && double.IsFinite(p.Value)), "Invalid difficulty-specific scalar scale.");
    }

    private static void ValidateBindings(Dictionary<string, GimmickScalar>? bindings, params string[] properties)
    {
        Require(bindings != null && bindings.Count <= properties.Length && bindings.Keys.All(properties.Contains), "Invalid drawing property binding.");
        foreach (var scalar in bindings.Values)
        {
            ValidateScalar(scalar);
            Require(scalar.Parameter == null, "Drawing bindings cannot depend on optional resource parameters.");
        }
    }

    private static bool ModName(string? name) => name == null || !string.IsNullOrWhiteSpace(name);

    private static void ValidateInterludes(List<NativeInterludeDefinition>? definitions, Func<string, bool> hasSprite)
    {
        Require(definitions != null && definitions.Count <= 16, "Invalid native interlude count.");
        foreach (var interlude in definitions)
        {
            Require(interlude != null && !string.IsNullOrWhiteSpace(interlude.Trigger) &&
                (interlude.StartSongTime == null || double.IsFinite(interlude.StartSongTime.Value) && interlude.StartSongTime >= 0) &&
                (interlude.BackgroundSprite == null || hasSprite(interlude.BackgroundSprite)) &&
                Finite(interlude.CharactersPerSecond, interlude.MinimumCueSeconds, interlude.PaddingSeconds) &&
                interlude.CharactersPerSecond is > 0 and <= 1000 && interlude.MinimumCueSeconds is > 0 and <= 3600 &&
                interlude.PaddingSeconds is >= 0 and <= 3600 && interlude.Steps != null && interlude.Steps.Count <= 512,
                "Invalid native interlude timing or background.");
            foreach (var step in interlude.Steps)
            {
                Require(step != null && step.Speaker is { Length: <= 256 } && (step.Text == null || step.Text.Length <= 32768) &&
                    (step.Duration == null || double.IsFinite(step.Duration.Value) && step.Duration is >= 0 and <= 3600) &&
                    step.Actions != null && step.Actions.Count <= 64, "Invalid native interlude step.");
                foreach (var action in step.Actions)
                {
                    Require(action != null && action.Kind is "background" or "background-destroy" or "portrait-create" or "portrait-change" or
                        "portrait-move" or "portrait-alpha" or "portrait-destroy" or "portrait-frame" or "portrait-flip" or "clear-text" or "tint-color" or "tint" or "bgm" or "sound" or "bgm-fade" or "audio-stop" or "audio-pitch" or "resume" &&
                        Finite(action.Scale, action.To, action.Duration, action.Gain) && action.Duration is >= 0 and <= 3600 &&
                        action.Gain is >= 0 and <= 4 && action.ColorBgr <= 0xffffff && action.Frame >= 0 &&
                        (action.X == null || double.IsFinite(action.X.Value)) && (action.Y == null || double.IsFinite(action.Y.Value)) &&
                        (action.From == null || double.IsFinite(action.From.Value)) &&
                        (action.Sprite == null || hasSprite(action.Sprite)), "Invalid native interlude action.");
                    if (action.Kind is "portrait-create" or "portrait-change" or "portrait-move" or "portrait-alpha" or "portrait-frame" or "portrait-flip" or "audio-pitch" or "audio-stop")
                        Require(!string.IsNullOrWhiteSpace(action.Id), "Missing interlude portrait identity.");
                    if (action.Kind == "audio-pitch") Require(action.To is >= .01 and <= 100, "Invalid interlude playback pitch.");
                    if (action.Kind is "background" or "portrait-create" or "portrait-change")
                        Require(action.Sprite != null && hasSprite(action.Sprite), "Missing interlude sprite.");
                    if (action.Kind is "bgm" or "sound")
                        Require(!string.IsNullOrWhiteSpace(action.Path) && !Path.IsPathRooted(action.Path) && !action.Path.Contains('\\') &&
                            !action.Path.Contains(':') && action.Path.Split('/').All(p => p is not ("" or "." or "..")), "Invalid interlude audio path.");
                }
            }
        }
    }

    /// <summary>合并别名和所有逐帧写入的依赖，再做三色 DFS。禁止任何潜在求值环。</summary>
    private static void ValidateReferenceGraph(GimmickDefinition definition)
    {
        var graph = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        void Edge(string from, string to)
        {
            if (!graph.TryGetValue(from, out var targets))
            {
                graph[from] = targets = new HashSet<string>(StringComparer.Ordinal);
            }
            targets.Add(to);
        }
        foreach (var (from, to) in definition.ModAliases)
        {
            Edge(from, to);
        }
        Require(definition.ValueBindings!=null,"Missing value bindings.");
        foreach (var bindings in definition.PerFrameBindings.Values.Append(definition.ValueBindings))
        {
            foreach (var (output, scalar) in bindings)
            {
                foreach (string input in scalar.Mods)
                {
                    Edge(output, input);
                }
                if (scalar.MultiplyMod != null)
                {
                    Edge(output, scalar.MultiplyMod);
                }
                foreach(string input in scalar.MultiplyMods) Edge(output,input);
            }
        }
        var states = new Dictionary<string, int>(StringComparer.Ordinal);
        void Visit(string name, int depth)
        {
            Require(depth <= 128, "Mod reference chain exceeds 128 entries.");
            // 三色状态：0=未访问（白），1=在当前 DFS 路径上（灰），2=已完成（黑）。
            // 重新踩到灰色即为环，必须在加载时报错，不能留到逐帧求值时递归爆栈。
            int state = states.GetValueOrDefault(name);
            Require(state != 1, "Cyclic mod reference: " + name);
            if (state == 2)
            {
                return;
            }
            states[name] = 1;
            if (graph.TryGetValue(name, out var targets))
            {
                foreach (string target in targets)
                {
                    Visit(target, depth + 1);
                }
            }
            states[name] = 2;
        }
        foreach (string name in graph.Keys)
        {
            Visit(name, 0);
        }
    }

    private static void ValidateBpm(double? bpm) => Require(bpm == null || double.IsFinite(bpm.Value) && bpm is > 0 and <= 10000,
        "BPM must be between 0 and 10000.");
    private static bool Finite(params double[] numbers) => numbers.All(double.IsFinite);
    private static void Require([System.Diagnostics.CodeAnalysis.DoesNotReturnIf(false)] bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidDataException(message);
        }
    }
}
