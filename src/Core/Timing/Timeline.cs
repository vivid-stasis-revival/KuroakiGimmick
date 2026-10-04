namespace KuroakiGimmick.Core;

/// <summary>
/// 无显示状态的时间轴。构建时按拍数和声明顺序整理轨道；查询不依赖上一帧，因此预览、倒拖和导出可以共享结果。
/// </summary>
public sealed partial class Timeline
{
    /// <summary>一条 tween 片段。Time/Duration 已换算成秒；Duration 非正表示立即赋值，直接取 To。</summary>
    public record Segment(double Time, double Duration, double From, double To, string Ease, ModEvent Event)
    {
        public double Value(double time) => Duration <= 0 ? To : From + (To - From) * Easings.Eval(Ease, (time - Time) / Duration);
    }

    /// <summary>一次回调触发。Time 单位秒；Index 是全局触发序号，既用于 LatestOnly 判定同一 drawing 的最后一次触发，也让稳定排序后保留声明顺序。</summary>
    public record Callback(double Time, string Name, double Value, int Index);
    /// <summary>slash_anycol 的持续生成区间；Start/End 为秒，Index 继承全局 callback 序号，用于确定性随机种子和同刻稳定顺序。</summary>
    public record SlashSpan(double Start, double End, int TickCount, int Index);
    /// <summary>烘焙好的粒子。Time/Death/Life 单位秒，但都由 60 Hz 逻辑 tick 推导；X/Y/Vx/Vy 在 320×180 逻辑空间，速度是每 tick 位移。</summary>
    public record Dust(double Time, float X, float Y, float Vx, float Vy, int Frame, float Hue, float Saturation, double Death,
        bool Burst = false, double Life = 2, bool FollowPower = false);
    public Dictionary<(string Name, int Proxy), List<Segment>> Tracks { get; } = [];
    public List<Callback> Callbacks { get; } = [];
    public List<SlashSpan> SlashSpans { get; } = [];
    public record LoreleiSlash(double Time, int Count, double Color, int Index);
    public List<LoreleiSlash> LoreleiSlashes { get; } = [];
    public List<Dust> Particles { get; } = [];
    public List<Dust> ObjectParticles { get; } = [];
    /// <summary>原版会跳过的控制项记录。这些事件不执行但保留在报告里，不当作解析错误，也不悄悄删掉。</summary>
    public record SourceNoOp(string Name, int Line, double Beat, string Reason);
    public List<SourceNoOp> SourceNoOps { get; } = [];
    public BpmMap Bpm { get; }
    public NativeGimmickProfile? Native { get; }
    /// <summary>时间轴末端（秒）：取音频时长与所有 tween、回调尾部占用的较大值，跳转和导出都以它为界。</summary>
    public double End { get; private set; }
    public double Scroll { get; }
    public string Difficulty { get; }
    readonly List<System.Numerics.Vector2> particleOffsets = [];
    readonly Chart chart;
    public Timeline(Chart c, ViewerProject project, double duration, GameFxProfile? fx = null, NativeGimmickProfile? native = null)
    {
        chart = c;
        Scroll = project.ScrollSpeed;
        Native = native;
        Difficulty = SongFiles.Difficulty(project.Chart ?? project.Gimmick ?? "").ToUpperInvariant();
        Bpm = new(c, project.Bpm, project.OffsetMs, native?.Data?.FixedBpm);
        End = duration;
        // freeze 的两个回调点分别落在事件起点和终点，后者可能排在后续事件之后，所以先攒起来最后统一排序。
        List<(double Time, double Value, ModEvent Event)> freezeLatches = [];
        int callbackSequence = 0;
        double loreleiColor = 16777215;
        // 事件先按拍、再按声明顺序展开，这是整条链路的稳定顺序来源。
        foreach (var e in c.Mods.OrderBy(x => x.Beat).ThenBy(x => x.Order))
        {
            if (Native?.Data?.SourceNoOps.TryGetValue(e.Name, out string? sourceReason) == true)
            { SourceNoOps.Add(new(e.Name, e.SourceLine, e.Beat, sourceReason)); continue; }
            if (CustomCompatibility.NoOpReason(e.Name, c, project) is { } reason)
            { SourceNoOps.Add(new(e.Name, e.SourceLine, e.Beat, reason)); continue; }
            // 拍 → 秒：起点查 BPM map，时长按事件起点所在分段的 BPM 折算。
            // 与原版一致，跨变速点的 tween 不重新积分，整段沿用起点的 BPM。
            double start = Bpm.Time(e.Beat), span = e.Duration * 60 / Bpm.BpmAtBeat(e.Beat);
            if (e.Name is "fx_underwater" or "fx_chroma_distort")
            {
                bool lowFrom = e.From != 573613 && e.From < .01, lowTo = e.To != 573613 && e.To < .01;
                if (lowFrom || lowTo)
                    c.Diagnostics.Add(new("fx", e.SourceLine,
                        $"{e.Name}: values below 0.01 black out the original game; preview and saved/exported VSM clamp them to 0.01."));
            }
            if (e.Name is "lr_slash" or "lr_slash_color")
            {
                if (c.ObjectName != "obj_custom_gimmick")
                {
                    SourceNoOps.Add(new(e.Name, e.SourceLine, e.Beat,
                        "Frollsy's Extra Gimmicks requires obj_custom_gimmick."));
                    continue;
                }
                if (e.Name == "lr_slash_color")
                {
                    if (e.To != 573613)
                    {
                        loreleiColor = e.To;
                        var colorKey = (e.Name, -1);
                        if (!Tracks.TryGetValue(colorKey, out var colors)) Tracks[colorKey] = colors = [];
                        colors.Add(new(start, 0, loreleiColor, loreleiColor, "linear", e));
                    }
                    End = Math.Max(End, start);
                }
                else
                {
                    int count = e.From == 573613 ? 1 : (int)Math.Clamp(Math.Floor(e.From), 1, 64);
                    LoreleiSlashes.Add(new(start, count, e.To == 573613 ? loreleiColor : e.To, e.Order));
                    End = Math.Max(End, start + 1);
                }
                continue;
            }
            if (Native?.Data?.Sequence is { } sequence && (e.Name == sequence.Mod("slash") || e.Name == sequence.Mod("gun")))
            {
                // slash 的实例数来自原版：1 + 时长毫秒数，取偶数舍入；gun 恒为 1 次。
                double count = e.Name == sequence.Mod("slash") ? Math.Max(0, Math.Round(1 + span * 1000, MidpointRounding.ToEven)) : 1;
                if (count > 4096) c.Diagnostics.Add(new("native-sequence", e.SourceLine, "Slash callback exceeds 4096 instances; skipped.", true));
                else { Callbacks.Add(new(start, e.Name, count, callbackSequence++)); End = Math.Max(End, start + 1); }
                continue;
            }
            if (c.ObjectName == "obj_custom_gimmick" && CustomCompatibility.IsSideCallback(e.Name))
            { Callbacks.Add(new(start, e.Name, e.To, callbackSequence++)); End = Math.Max(End, start + 1); continue; }
            End = Math.Max(End, start + span);
            // v1.12.7 的 updateMods 会跳过未注册的 ID。只有这一个经过核实的
            // 旧拼写被归类为 no-op，其余未知 mod 仍然进诊断。
            // 实际的着色由带后缀的那几个 mod 驱动。
            if (c.ObjectName == "obj_custom_gimmick" && e.Name == "fx_colorise")
            {
                SourceNoOps.Add(new(e.Name, e.SourceLine, e.Beat,
                    "Custom Gimmicks v1.12.7: unregistered ID is skipped by updateMods; the three suffixed colorise controls remain active."));
                continue;
            }
            // 侧边/底部粒子爆发的可见寿命固定 4 秒和 2 秒（plaudite 再加上倒计时毫秒），
            // 这些是原版数值，时间轴尾部必须留够，否则最后一次爆发会被裁掉。
            if (e.Name is "pburstleft" or "pburstright")
            {
                End = Math.Max(End, start + 4);
            }
            else if (e.Name == "plaudite_pburst")
            {
                End = Math.Max(End, start + 2 + (e.From > 0 ? Math.Max(0, e.To) / 1000 : 0));
            }
            // freeze 注册了 start/end 回调，于是 updateMods 走 CreateChartCallback 而不是 CreateChartTween：
            // 它根本不插值，缓动和 proxy 也一并不看，所以这一段放在那两项校验之前，和 slash/sides 一样。
            // 起点回调在 v1 为 0 时清零、否则把 cc.mod_freeze 写成事件起点的毫秒数；终点回调对 v2 做同样判断，
            // 写入的同样是事件起点而不是终点。因此 from/to 只当开关用，冻结到的时刻永远是这条事件自己的起点。
            // 573613 这个 "_" 哨兵也不解析：原版把它原样交给回调，非 0 即视为开启。
            if (e.Name == "freeze")
            {
                freezeLatches.Add((start, e.From == 0 ? 0 : start * 1000, e));
                freezeLatches.Add((start + span, e.To == 0 ? 0 : start * 1000, e));
                continue;
            }
            if (!Easings.IsKnown(e.Ease))
            {
                c.Diagnostics.Add(new("timeline", e.Order + 1, $"Unsupported easing {e.Ease}; event not rendered.", true));
                continue;
            }
            if (e.Proxy >= c.Proxies)
            {
                c.Diagnostics.Add(new("timeline", e.Order + 1, $"Proxy {e.Proxy} is outside !proxies:{c.Proxies}.", true));
                continue;
            }
            if (e.Name == "slash_anycol" && e.To > 0)
            {
                if (span <= 0)
                {
                    // 零时长仍是一次性 callback，保持旧谱面与既有随机种子行为。
                    Callbacks.Add(new(start, e.Name, e.To, callbackSequence++));
                    End = Math.Max(End, start + 1);
                }
                else
                {
                    // 本体不是把一条 slash 拉长，而是在区间内每个 60 Hz 逻辑 tick 生成一条新 slash。
                    // 这里只保存区间；绘制时仅枚举最近 1 秒仍存活的实例，避免长 duration 预分配海量 Callback（#33）。
                    double rawTicks = Math.Ceiling(span * 60);
                    int tickCount = rawTicks >= int.MaxValue ? int.MaxValue : Math.Max(1, (int)rawTicks);
                    SlashSpans.Add(new(start, start + span, tickCount, callbackSequence++));
                    End = Math.Max(End, start + span + 1);
                }
                continue;
            }
            if (e.Name == "recolor")
            {
                var hueKey = ("curcolor", -1);
                if (!Tracks.TryGetValue(hueKey, out var hues))
                {
                    Tracks[hueKey] = hues = [];
                }
                // 色相由事件序号确定性地导出，因此同一份谱面每次加载得到同样的配色。
                double hue = Math.Floor(Hash(unchecked((uint) e.Order * 17 + 9127)) * 256);
                hues.Add(new(start, 0, hue, hue, "linear", e));
                continue;
            }
            if (Native?.Data?.Callbacks.TryGetValue(e.Name, out var drawings) == true)
            {
                if (Native.Data.CallbackConstraints.TryGetValue(e.Name, out var constraint) && !constraint.Accepts(e.To))
                {
                    c.Diagnostics.Add(new("native-gimmick/" + e.Name, e.SourceLine,
                        $"Callback value must be {(constraint.Integer ? "an integer " : "")}{constraint.Minimum}..{constraint.Maximum}; event skipped.", true));
                    continue;
                }
                // 可见动画可能先离开屏幕；事件尾部仍遵守声明的原始回调寿命。
                // CallbackLifetimes 是时间轴尾部占用，GimmickDrawing.Lifetime 是可见绘制寿命，两者不混用：
                // 侧边回调占用 0.4 秒尾部，可见绘制只有 0.3875 秒；为“简化”把两者统一会改变最后一个回调之后的时间轴。
                double lifetime = Native.Data.CallbackLifetimes.GetValueOrDefault(e.Name, drawings.Max(d => d.Lifetime));
                End = Math.Max(End, start + lifetime);
                Callbacks.Add(new(start, e.Name, e.To, callbackSequence++));
                continue;
            }
            var key = (e.Name, e.Proxy);
            if (!Tracks.TryGetValue(key, out var list))
            {
                Tracks[key] = list = [];
            }
            // 573613 是 VSM 里 "_" 的原版哨兵：取该时刻轨道上的当前值作为端点。
            // checker_set 的起点固定按 0 算，而不是沿用已有轨道值。
            var atStart = e.Name == "angelstar_checker_set" ? 0 : Evaluate(list, start, Initial(e.Name, e.Proxy));
            var from = e.From == 573613 ? atStart : e.From;
            var to = e.To == 573613 ? atStart : e.To;
            // 激活/枚举型控制是瞬时写入：时长归零并直接跳到 To，不做插值。
            // changeskin 选择的是离散皮肤下标；把 from->to 当普通 tween 会在一个事件里
            // 依次穿过中间皮肤，和实际的整套皮肤切换语义不符。
            bool activation = e.Name is "active_startrans" or "active_starchgcol" or "changeskin";
            list.Add(new(start, activation ? 0 : span, activation ? to : from, to, Easings.Normalize(e.Ease), e));
        }
        if (freezeLatches.Count > 0)
        {
            // Evaluate 是二分查找，轨道必须升序；OrderBy 稳定，同一时刻仍按声明顺序，后声明的覆盖先声明的。
            // 每个点都是时长为 0 的瞬时写入，取值单位是毫秒，和原版的 cc.mod_freeze 一致。
            Tracks[("freeze", -1)] = freezeLatches.OrderBy(p => p.Time)
                .Select(p => new Segment(p.Time, 0, p.Value, p.Value, "linear", p.Event)).ToList();
        }
        // drawdist 在原版里也会被 tween 进 cc.mod_drawdist，但实时绘制的 obj_note_rendering 不读它，
        // 读它的只有没有调用方的 obj_noteNormal 实例池；cc 的 Create 里甚至没初始化这个变量。
        // 所以这条轨道在原版同样毫无效果，如实说明而不是假装画了什么。
        if (c.Mods.Any(e => e.Name == "drawdist"))
        {
            c.Diagnostics.Add(new("renderer", 0, "drawdist: evaluated onto its track, but the original build's live note "
                + "renderer never reads it (only the retired obj_noteNormal pooling path does), so it has no visible effect here either."));
        }
        var ignoredEvents = SourceNoOps.Select(n => (n.Line, n.Name)).ToHashSet();
        var scopedNames = SourceNoOps.Where(n => n.Name != "fx_colorise").Select(n => n.Name).Distinct().ToArray();
        if (scopedNames.Length > 0) c.Diagnostics.Add(new("compatibility/scope", 0,
            "Original game skips these controls for this object/configuration: " + string.Join(", ", scopedNames) + ". See sourceNoOps in the report for source evidence."));
        foreach (var group in c.Mods.Where(e => !ignoredEvents.Contains((e.SourceLine, e.Name)) && !(c.ObjectName == "obj_custom_gimmick" && e.Name == "fx_colorise")
            && !ModCatalog.Supported.Contains(e.Name) && Native?.Supports(e.Name) != true && !CustomImages.TryMod(e.Name, out _, out _)
            && !CustomText.TryMod(e.Name, out _, out _)).GroupBy(e => System.Text.RegularExpressions.Regex.Replace(e.Name, @"\d+$", "*")))
        {
            c.Diagnostics.Add(new("renderer", 0,
                $"Unsupported mod: {group.Key} ({group.Count()} events, {group.Select(e=>e.Name).Distinct().Count()} names retained)."));
        }
        var missingImages = c.Mods.Select(e => CustomImages.TryMod(e.Name, out _, out var id) ? id : null).OfType<string>().Distinct()
            .Where(id => !c.ImageNames.Contains(id)).ToArray();
        if (missingImages.Length > 0)
        {
            c.Diagnostics.Add(new("images", 0,
                $"{missingImages.Length} undeclared image IDs; load matching .vsp and image assets. First: {string.Join(", ",missingImages.Take(8))}"));
        }
        var missingTexts = c.Mods.Where(e => CustomText.TryMod(e.Name, out var kind, out var id) && !c.TextNames.Contains(id)
            && !(kind is "textX" or "textY" && id.EndsWith('b') && c.TextNames.Contains(id[..^1]))).Select(e => e.Name).Distinct().ToArray();
        if (missingTexts.Length > 0)
        {
            c.Diagnostics.Add(new("text", 0, $"Text tracks without matching text files: {string.Join(", ",missingTexts.Take(8))}"));
        }
        foreach (var f in c.PerFrame)
        {
            if (Native?.Data?.PerFrameBindings.ContainsKey(f.Function) != true)
            {
                c.Diagnostics.Add(new("renderer", 0, $"Unsupported per-frame function: {f.Function}"));
            }
        }
        if (Native?.Data?.CompleteObject != true && c.ObjectName is not ("obj_base_gimmick" or "obj_custom_gimmick"))
        {
            c.Diagnostics.Add(new("renderer", 0, $"{c.ObjectName}: common mods only; its custom object renderer is not implemented."));
        }
        bool screenGlow = Tracks.Where(k => k.Key.Name == "fx_glow").SelectMany(k => k.Value).Any(s => s.To > 0 || s.Duration > 0 && s.From > 0);
        if (!File.Exists(Path.Combine(Paths.Assets, "GameFX", "sp_noise2.png")) && Native?.Supports("uhnoise") != true && c.Mods.Any(e => e.Name == "uhnoise" && (e.To != 0 || e.From != 0)))
        {
            c.Diagnostics.Add(new("renderer", 0,
                "Horizontal noise uses the custom shader with deterministic substitute noise; exact original texture is unavailable."));
        }
        if (fx?.Find("FX_underwater") == null && Tracks.Where(k => k.Key.Name == "fx_underwater").SelectMany(k => k.Value).Any(s => s.To != 0
            || s.Duration > 0 && s.From != 0))
        {
            c.Diagnostics.Add(new("renderer", 0,
                "Underwater uses the original filter and noise texture with fallback layer settings; exact FX_underwater room settings are unavailable. An external profile can now supply this layer."));
        }
        // 定义了但未使用、或只在特定场景生效的控制项，也要在报告里可见。
        foreach (var group in c.Mods.Where(e => e.Name is "video" or "cover1" or "cover2" or "cover3" or "spinradius" or "spiny" or "spinx")
            .GroupBy(e => e.Name))
        {
            if (Native?.Supports(group.Key) != true && !(c.ObjectName == "obj_custom_gimmick" && group.Key is "cover1" or "cover2" or "cover3") && group.Any(e => e.To != 0))
            {
                c.Diagnostics.Add(new("renderer", 0, $"{group.Key}: value evaluated, drawing behavior not implemented in v0.1.0."));
            }
        }
        // 在任何粒子预计算前建立逐帧索引，保证所有发射器看到同一套 mod 求值规则。
        IndexObjectBehavior();
        // 粒子最长只烘焙到 3600 秒，避免异常时长的工程把内存吃光；其余查询仍按 End 走。
        // o_csm_particle_system 属于 base/custom 共用的玩法场景能力，不能把 ambient dust 错绑到 custom object（#31）。
        bool ambientParticles = c.ObjectName is "obj_base_gimmick" or "obj_custom_gimmick"
            || Native?.Data?.AmbientParticles == true;
        bool hasSideBursts = c.Mods.Any(e => e.Name is "pburstleft" or "pburstright");
        bool needsParticleOffsets = ambientParticles || hasSideBursts
            || c.Mods.Any(e => e.Name is "particlexpower" or "particleypower");
        if (needsParticleOffsets)
        {
            BuildCustomParticles(Math.Min(End, 3600), emit: ambientParticles);
            if (c.ObjectName == "obj_custom_gimmick")
            {
                BuildBursts(Math.Min(End, 3600));
            }
            if (hasSideBursts)
            {
                BuildSideBursts(Math.Min(End, 3600));
            }
            Particles.Sort((a, b) => a.Time.CompareTo(b.Time));
        }
        if (screenGlow && fx?.Find("FX_glow") == null)
        {
            c.Diagnostics.Add(new("renderer", 0, "Scene glow requires the original FX_glow layer settings; the missing layer is not rendered."));
        }
        if ((ambientParticles || c.Mods.Any(e => e.Name is "pburstleft" or "pburstright" or "fx_particleglow"))
            && fx?.Find("glow") == null)
        {
            c.Diagnostics.Add(new("renderer", 0,
                "Background particle glow requires the original glow layer settings; the missing layer is not rendered."));
        }
        if (Native?.Data?.ParticleEmitter is { } emitter && Native.Sprites.ContainsKey(emitter.Sprite))
        {
            BuildObjectParticles(Math.Min(End, 3600), emitter);
        }
    }

    /// <summary>该 mod 在任何事件之前的初值：全局轨道优先用对象定义的默认值，其余走基础协议表。</summary>
    double Initial(string name, int proxy) => proxy == -1 && Native?.Data?.Defaults.TryGetValue(name,
        out var value) == true ? value : ModCatalog.Default(name, proxy, Scroll);
    /// <summary>
    /// 确定性整数散列，返回 [0,1)。粒子、色相等一切“随机”都由它从序号导出，
    /// 因此回放、拖动和导出得到同一结果；换成 Random 会让同一份谱面每次不同。
    /// </summary>
    public static float Hash(uint n)
    {
        unchecked
        {
            n = (n ^ n >> 16) * 0x45d9f3b;
            n = (n ^ n >> 16) * 0x45d9f3b;
            return (n ^ n >> 16) / 4294967296f;
        }
    }
}
