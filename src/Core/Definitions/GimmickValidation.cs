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
        Require(definition.ParameterObjectPath != null, "ParameterObjectPath must not be null.");
        Require(definition.SourceNoOps != null && definition.SourceNoOps.Count <= 128 && definition.SourceNoOps.All(p => !string.IsNullOrWhiteSpace(p.Key) && !string.IsNullOrWhiteSpace(p.Value)), "Invalid documented source no-ops.");
        if (definition.Sequence is { } sequence)
        {
            Require(definition.Sprites != null && sequence.Sprites != null && sequence.Mods != null && sequence.BackgroundLayers != null,
                "Invalid native sequence roles.");
            Require(sequence.Sprites.Count <= 32 && sequence.Mods.Count <= 32 && sequence.BackgroundLayers.Count <= 8,
                "Native sequence role limits exceeded.");
            Require(sequence.Sprites.Values.All(definition.Sprites.ContainsKey) && sequence.Mods.Values.All(n => !string.IsNullOrWhiteSpace(n)), "Undeclared sequence sprite or mod.");
            Require(sequence.Story != null && sequence.Story.Count <= 1024 && sequence.Story.All(c => double.IsFinite(c.Time) && c.Time >= 0 &&
                !string.IsNullOrWhiteSpace(c.Trigger) && c.Speaker != null && c.Text is { Length: <= 32768 }), "Invalid native story cues.");
            Require(sequence.StoryWindows != null && sequence.StoryWindows.Count <= 128 && sequence.StoryWindows.All(w =>
                !string.IsNullOrWhiteSpace(w.Trigger) && double.IsFinite(w.Start) && w.Start >= 0 &&
                (w.Clear == null || double.IsFinite(w.Clear.Value) && w.Clear >= w.Start) &&
                (w.Destroy == null || double.IsFinite(w.Destroy.Value) && w.Destroy >= (w.Clear ?? w.Start))), "Invalid native story windows.");
            Require(sequence.EndFadeTime == null || double.IsFinite(sequence.EndFadeTime.Value) && sequence.EndFadeTime >= 0 &&
                double.IsFinite(sequence.EndFadeDuration) && sequence.EndFadeDuration > 0 && !string.IsNullOrWhiteSpace(sequence.EndFadeTrigger), "Invalid sequence end fade.");
        }
        Require(definition.Version == 1 && !string.IsNullOrWhiteSpace(definition.ObjectName), "Invalid object definition version/name.");
        // 扩展表按位置编码，旧格式允许不同 ID 对应同名 mod；禁止去重，否则后续 ID 全部错位。
        Require(definition.ExtraMods != null && definition.ExtraMods.Length <= 127
            && definition.ExtraMods.All(name => !string.IsNullOrWhiteSpace(name)), "At most 127 binary mod IDs are allowed.");
        Require(definition.ProfileAliases != null && definition.ProfileAliases.Length <= 32 && definition.PreviewMods != null
            && definition.PreviewMods.Length <= 16 && definition.ProfileAliases.All(name => !string.IsNullOrWhiteSpace(name))
            && definition.PreviewMods.All(name => !string.IsNullOrWhiteSpace(name)), "Invalid profile aliases or preview mods.");
        Require(definition.Defaults != null && definition.Defaults.Count <= 512 && definition.Defaults.Values.All(double.IsFinite),
            "Invalid mod defaults.");
        Require(definition.ModAliases != null && definition.ModAliases.Count <= 128
            && definition.ModAliases.All(pair => !string.IsNullOrWhiteSpace(pair.Key) && !string.IsNullOrWhiteSpace(pair.Value)),
            "Invalid mod aliases.");
        Require(definition.Sprites != null && definition.Sprites.Count <= 64 && definition.RequiredSprites != null
            && definition.RequiredSprites.Count <= 64 && definition.Textures != null && definition.Textures.Count <= 16
            && definition.Shaders != null && definition.Shaders.Count <= 16, "Invalid resource dictionary/count.");
        Require(definition.Callbacks != null && definition.Callbacks.Count <= 128 && definition.Callbacks.Values.All(items => items != null
            && items.Count is >= 1 and <= 64) && definition.Overlays != null && definition.Overlays.Count <= 64, "Invalid callbacks/overlays.");
        Require(definition.CallbackConstraints != null && definition.CallbackConstraints.Count <= 128 && definition.CallbackFades != null
            && definition.CallbackFades.Count <= 128 && definition.PerFrameBindings != null && definition.PerFrameBindings.Count <= 32,
            "Invalid timeline bindings.");
        Require(definition.PostModes != null && definition.PostModes.Count <= 16 && definition.CheckerMode is >= 0 and <= 2,
            "Invalid shader modes/checkerboard mode.");
        ValidateBpm(definition.InitialBpm);
        ValidateBpm(definition.FixedBpm);
        bool HasSprite(string name) => !string.IsNullOrWhiteSpace(name) && (definition.Sprites.ContainsKey(name)
            || definition.RequiredSprites.ContainsKey(name));
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
            Require(definition.Callbacks.ContainsKey(name) && double.IsFinite(lifetime) && lifetime is > 0 and <= 60
                && lifetime >= definition.Callbacks[name].Max(drawing => drawing.Lifetime), "Callback lifetime must cover its drawings: " + name);
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
            Require(mode != null && definition.Shaders.ContainsKey(mode.Shader) && mode.Uniforms != null && mode.Uniforms.Count <= 64
                && mode.Samplers != null && mode.Samplers.Count <= 8 && mode.LinearSamplers != null
                && mode.LinearSamplers.All(mode.Samplers.ContainsKey), "Invalid shader/sampler binding.");
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

    /// <summary>枚举所有 shader pass 及其组件名；组件名同时是逐组件错误记录的键，改名会改变诊断标识。</summary>
    public static IEnumerable<(string Component, GimmickShaderMode Mode)> ShaderModes(GimmickDefinition definition)
    {
        foreach (var (index, mode) in definition.PostModes)
        {
            yield return ("post/" + index, mode);
        }
        if (definition.Background?.Effect is { } background)
        {
            yield return ("background/" + background.Shader, background);
        }
    }

    /// <summary>
    /// 校验 manifest 声明的精灵几何：尺寸 1..4096、原点必须落在尺寸之内、帧数 1..64、Scope 限 pack/definition/shared。
    /// 这里只看声明值；解码后的纹理像素尺寸由加载阶段另行比对。
    /// </summary>
    public static void ValidateSprite(GimmickSprite? sprite)
    {
        Require(sprite != null && sprite.Width is >= 1 and <= 4096 && sprite.Height is >= 1 and <= 4096 && sprite.Frames != null
            && sprite.Frames.Count is >= 1 and <= 64 && sprite.OriginX >= 0 && sprite.OriginX <= sprite.Width && sprite.OriginY >= 0
            && sprite.OriginY <= sprite.Height && sprite.Scope is null or "pack" or "definition" or "shared",
            "Invalid sprite dimensions, origin, frames or scope.");
    }

    private static void ValidateDrawing(GimmickDrawing? drawing, Func<string, bool> hasSprite)
    {
        Require(drawing != null && hasSprite(drawing.Sprite) && GimmickStages.All.Contains(drawing.Stage) && drawing.Tweens != null
            && drawing.Tweens.Count <= 32 && drawing.SuppressRanges != null && drawing.SuppressRanges.Count <= 64 && Finite(drawing.X, drawing.Y,
            drawing.ScaleX, drawing.ScaleY, drawing.Alpha, drawing.Angle, drawing.VelocityX, drawing.VelocityY, drawing.FramesPerSecond,
            drawing.Lifetime) && drawing.Lifetime is > 0 and <= 60 && drawing.FramesPerSecond is >= 0 and <= 1000 && drawing.Frame >= 0
            && (drawing.EventValue == null || double.IsFinite(drawing.EventValue.Value)), "Invalid drawing sprite, stage or transform.");
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
        Require(scalar != null && scalar.Mods != null && scalar.Mods.Length <= 8 && scalar.Mods.All(mod => !string.IsNullOrWhiteSpace(mod))
            && scalar.Wave is null or "sin" or "cos" && Finite(scalar.Constant, scalar.TimeScale, scalar.BeatScale, scalar.Scale)
            && scalar.ParameterIndex is >= -1 and <= 3 && (scalar.ParameterMinimumExclusive == null
            || double.IsFinite(scalar.ParameterMinimumExclusive.Value)) && (scalar.Wrap == null || double.IsFinite(scalar.Wrap.Value)
            && scalar.Wrap > 0), "Invalid scalar binding.");
        Require(scalar.DifficultyScale == null || scalar.DifficultyScale.Count <= 8 && scalar.DifficultyScale.All(p => !string.IsNullOrWhiteSpace(p.Key) && double.IsFinite(p.Value)), "Invalid difficulty-specific scalar scale.");
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
        foreach (var bindings in definition.PerFrameBindings.Values)
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
