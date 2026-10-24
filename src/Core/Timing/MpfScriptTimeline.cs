using System.Globalization;

namespace KuroakiGimmick.Core;

/// <summary>
/// Deterministic, bounded Custom Gimmicks 1.14.1 MPF preview. Programs run on the
/// fixed 60 Hz preview clock into immutable sample tracks; seeking, snapshots,
/// parallel export and Rust base-track evaluation cannot advance script state.
/// Score and input queries use an explicit offline baseline. Script judgment
/// changes share one state; they do not represent live player input or autoplay.
/// </summary>
internal sealed class MpfScriptTimeline
{
    sealed record Write(double Time, double Value, double? TweenRegistered = null);
    sealed record DeferredSet(double Beat, string Name, int Proxy, double Value, int Order);
    sealed record DeferredTween(double Start, double End, string Ease, string Name, int Proxy, double From, double To, double Registered, int Order);

    sealed class Script(string name, string path, MpfScriptLanguage language)
    {
        internal string Name = name;
        internal string Path = path;
        internal MpfScriptLanguage Language = language;
        internal MpfScriptLanguage.Context Context = new();
        internal bool Initialized;
        internal bool Dead;
        internal int DebugMessages;
        internal bool OfflineStateNotice;
    }
    sealed class ActiveRange(FrameEvent range, Script script)
    {
        internal FrameEvent Range = range;
        internal Script Script = script;
        internal bool Active;
    }

    Timeline timeline;
    readonly Chart chart;
    readonly MpfPreviewGameState game;
    bool inputNotice;
    readonly Dictionary<string, Script> scripts = new(StringComparer.Ordinal);
    readonly Dictionary<(string Name, int Proxy), List<Write>> writes = [];
    readonly PriorityQueue<DeferredSet, (double Beat, int Order)> pendingSets = new();
    readonly PriorityQueue<DeferredTween, (double Time, int Order)> pendingTweens = new();
    readonly SortedDictionary<int, DeferredTween> activeTweens = [];
    internal double End { get; private set; }
    bool durationNotice;
    double currentTime, currentBeat;
    string phase = "main";
    int writeCount, scheduledCount, currentTick;

    MpfScriptTimeline(Timeline timeline, Chart chart)
    {
        this.timeline = timeline;
        this.chart = chart;
        game = new(new ScoreState(chart, timeline.Bpm).NoteValue);
    }

    Random random = new(0);
    bool randomNotice;
    void WarnRandom()
    {
        if (randomNotice) return;
        randomNotice = true;
        chart.Diagnostics.Add(new("cmpf", 0, "MPF uses one deterministic offline RNG shared by scripts; its sequence is not guaranteed to match GameMaker or other game systems."));
    }
    static double Num(object? value) => value is double d && double.IsFinite(d)
        ? d : throw new InvalidDataException("TypeError: numeric argument required.");
    static string Name(object? value) => value is string s && s.Length is > 0 and <= 128
        ? s : throw new InvalidDataException("TypeError: nonempty string argument required.");
    int Proxy(object?[] args, int index)
    {
        if (index >= args.Length) return -1;
        double raw = Num(args[index]);
        if (Math.Truncate(raw) != raw || raw is < -1 or > 63 || raw >= chart.Proxies)
            throw new InvalidDataException("IndexError: proxy must be -1 or within the declared proxy count.");
        return (int)raw;
    }
    static void Count(string name, object?[] args, int min, int max)
    {
        if (args.Length < min || args.Length > max)
            throw new InvalidDataException("TypeError: " + name + " expects " + min + (min == max ? "" : ".." + max) + " arguments.");
    }
    internal MpfScriptTimeline ForkForRender(Timeline owner)
    {
        var copy = (MpfScriptTimeline)MemberwiseClone(); copy.timeline = owner; return copy;
    }

    bool WinsAgainstNative(string name, int proxy, double time, Write write)
    {
        double tick = Math.Floor(time * 60 + 1e-8) / 60;
        double nativeWrite = timeline.NativeTrackWriteTime(name, proxy, tick);
        if (nativeWrite < write.Time - 1e-9) return true;
        // updateMods registers VSM tweens before MPF executes. Both then run in registration
        // order, so a later MPF tween wins ties, but an older future tween does not.
        return write.TweenRegistered is double registered && nativeWrite <= write.Time + 1e-9
            && Math.Ceiling(timeline.NativeTrackStartTime(name, proxy, tick) * 60 - 1e-8) / 60 <= registered + 1e-9;
    }
    double ValueAt(string name, int proxy, double time)
    {
        if (writes.TryGetValue((name, proxy), out var list) && list.Count > 0 && list[^1].Time == currentTime)
            return list[^1].Value; // earlier direct writes in this frame are already visible
        double previousTick = (currentTick - 1) / 60.0;
        return TryValue(name, previousTick, proxy, out double value) ? value : timeline.NativeRawValue(name, previousTick, proxy);
    }
    void WriteMod(string name, int proxy, double value, double? tweenRegistered = null)
    {
        if (!double.IsFinite(value)) throw new InvalidDataException("ValueError: non-finite MPF mod value.");
        if (++writeCount > 1_000_000) throw new InvalidDataException("MaxLoopReached: MPF preview output exceeds one million writes.");
        var key = (name, proxy);
        if (!writes.TryGetValue(key, out var track)) writes[key] = track = [];
        if (track.Count > 0 && track[^1].Time == currentTime) track[^1] = new(currentTime, value, tweenRegistered);
        else track.Add(new(currentTime, value, tweenRegistered));
    }
    void ScheduleSet(string name, int proxy, double beat, double value)
    {
        if (!double.IsFinite(beat) || !double.IsFinite(value)) throw new InvalidDataException("ValueError: invalid scheduled MPF value.");
        if (++scheduledCount > 100_000) throw new InvalidDataException("MaxLoopReached: too many scheduled MPF actions.");
        pendingSets.Enqueue(new(beat, name, proxy, value, scheduledCount), (beat, scheduledCount));
        ExtendEnd(timeline.Bpm.Time(beat));
    }
    void ScheduleTween(string name, int proxy, double startBeat, double durationBeats, string ease, double from, double to)
    {
        if (!double.IsFinite(startBeat) || !double.IsFinite(durationBeats) || !double.IsFinite(from) || !double.IsFinite(to))
            throw new InvalidDataException("ValueError: invalid MPF tween.");
        if (!Easings.IsKnown(ease)) throw new InvalidDataException("NameError: unknown easing '" + ease + "'.");
        if (++scheduledCount > 100_000) throw new InvalidDataException("MaxLoopReached: too many MPF tweens.");
        double start = timeline.Bpm.Time(startBeat);
        double end = start + Math.Max(0, durationBeats) * 60 / timeline.Bpm.BpmAtBeat(startBeat);
        pendingTweens.Enqueue(new(start, end, ease, name, proxy, from, to, currentTime, scheduledCount), (start, scheduledCount));
        ExtendEnd(Math.Max(start, end));
    }
    void ExtendEnd(double time)
    {
        if (!double.IsFinite(time) || time > 3600)
        {
            if (!durationNotice) chart.Diagnostics.Add(new("cmpf", 0, "MPF preview is limited to 3600 seconds; later scheduled actions are not simulated.", true));
            durationNotice = true;
        }
        End = Math.Max(End, Math.Min(time, 3600));
    }
    void ApplySchedules()
    {
        var due = new List<DeferredSet>();
        while (pendingSets.TryPeek(out var set, out var priority) && priority.Beat <= currentBeat + 1e-8)
        { pendingSets.Dequeue(); due.Add(set); }
        foreach (var set in due.OrderBy(set => set.Order)) WriteMod(set.Name, set.Proxy, set.Value);
        while (pendingTweens.TryPeek(out var tween, out var priority) && priority.Time <= currentTime + 1e-8)
        { pendingTweens.Dequeue(); activeTweens.Add(tween.Order, tween); }
        List<int>? completed = null;
        foreach (var tween in activeTweens.Values)
        {
            double progress = tween.End <= tween.Start ? 1 : Math.Clamp((currentTime - tween.Start) / (tween.End - tween.Start), 0, 1);
            WriteMod(tween.Name, tween.Proxy, tween.From + (tween.To - tween.From) * Easings.Eval(tween.Ease, progress), tween.Registered);
            if (progress >= 1) (completed ??= []).Add(tween.Order);
        }
        if (completed != null) foreach (int order in completed) activeTweens.Remove(order);
    }
    void WarnOfflineState(Script script)
    {
        if (script.OfflineStateNotice) return;
        script.OfflineStateNotice = true;
        chart.Diagnostics.Add(new(script.Path, 0,
            "MPF " + script.Name + ": zeroed offline preview counters are shared and updated by script judgment calls; "
            + "live player input, autoplay hits and real AC/FC state are NOT simulated."));
    }

    object? Invoke(Script script, string function, object?[] a)
    {
        static List<object?> ArrayArg(object?[] a, int i) =>
            a[i] as List<object?> ?? throw new InvalidDataException("TypeError: array argument required.");
        static string Format(object? arg) =>
            arg is double number ? number.ToString("G", CultureInfo.InvariantCulture) : arg?.ToString() ?? "undefined";
        switch (function)
        {
            case "sin": Count(function,a,1,1); return Math.Sin(Num(a[0]));
            case "cos": Count(function,a,1,1); return Math.Cos(Num(a[0]));
            case "tan": Count(function,a,1,1); return Math.Tan(Num(a[0]));
            case "abs": Count(function,a,1,1); return Math.Abs(Num(a[0]));
            case "floor": Count(function,a,1,1); return Math.Floor(Num(a[0]));
            case "ceil": Count(function,a,1,1); return Math.Ceiling(Num(a[0]));
            case "min": Count(function,a,2,2); return Math.Min(Num(a[0]), Num(a[1]));
            case "max": Count(function,a,2,2); return Math.Max(Num(a[0]), Num(a[1]));
            case "clamp": Count(function,a,3,3); return Math.Clamp(Num(a[0]), Num(a[1]), Num(a[2]));
            case "to_str": Count(function,a,1,1); return Format(a[0]);
            case "str_cat":
            {
                Count(function,a,2,2); string left = Format(a[0]), right = Format(a[1]);
                script.Context.Allocate(checked(left.Length + right.Length)); return left + right;
            }
            case "array_create":
            {
                Count(function,a,1,1); double count = Num(a[0]);
                if (count < 0 || count > 100_000 || Math.Truncate(count) != count) throw new InvalidDataException("IndexError: array size is out of range.");
                script.Context.Allocate((int)count);
                return Enumerable.Repeat<object?>(0d, (int)count).ToList();
            }
            case "array_length": Count(function,a,1,1); return (double)ArrayArg(a,0).Count;
            case "array_contains": Count(function,a,2,2); return ArrayArg(a,0).Contains(a[1]) ? 1d : 0d;
            case "array_push":
            {
                Count(function,a,2,2); var array = ArrayArg(a,0);
                if (array.Count >= 100_000) throw new InvalidDataException("IndexError: array exceeds 100000 elements.");
                script.Context.Allocate(1); array.Add(a[1]); return array;
            }
            case "array_insert":
            {
                Count(function,a,3,3); var array = ArrayArg(a,0);
                double raw = Num(a[2]);
                if (raw < 0 || raw > array.Count || Math.Truncate(raw) != raw || array.Count >= 100_000)
                    throw new InvalidDataException("IndexError: cannot insert into array.");
                script.Context.Allocate(1); array.Insert((int)raw, a[1]); return array;
            }
            case "array_pop":
            {
                Count(function,a,1,1); var array = ArrayArg(a,0);
                if (array.Count == 0) throw new InvalidDataException("IndexError: array is empty.");
                object? result = array[^1]; array.RemoveAt(array.Count - 1); return result;
            }
            case "array_sort":
            {
                Count(function,a,2,2); var array = ArrayArg(a,0);
                bool ascending = Num(a[1]) > 0;
                array.Sort((x,y) => x is double n1 && y is double n2
                    ? n1.CompareTo(n2) * (ascending ? 1 : -1)
                    : string.Compare(Format(x), Format(y), StringComparison.Ordinal) * (ascending ? 1 : -1));
                return array;
            }
            case "random": Count(function,a,1,1); WarnRandom(); return random.NextDouble() * Num(a[0]);
            case "irandom":
            {
                Count(function,a,1,1); double upper = Num(a[0]);
                if (upper < 0 || upper > int.MaxValue - 1 || Math.Truncate(upper) != upper)
                    throw new InvalidDataException("ValueError: invalid irandom upper bound.");
                WarnRandom(); return (double)random.Next((int)upper + 1);
            }
            case "random_set_seed":
                Count(function,a,1,1); random = new(unchecked((int)Num(a[0]))); return 0d;
            case "ease":
                Count(function,a,4,4);
                if (!Easings.IsKnown(Name(a[0]))) throw new InvalidDataException("NameError: unknown easing.");
                return Num(a[1]) + (Num(a[2])-Num(a[1])) * Easings.Eval(Name(a[0]), Num(a[3]));
            case "register_mod":
            {
                Count(function,a,2,2);
                if (phase != "init") throw new InvalidDataException("PermissionError: register_mod is Init only.");
                string mod = Name(a[0]); WriteMod(mod, -1, Num(a[1])); return 0d;
            }
            case "set_mod_val":
                Count(function,a,2,3); WriteMod(Name(a[0]), Proxy(a,2), Num(a[1])); return 0d;
            case "get_mod_val":
                Count(function,a,1,2); return ValueAt(Name(a[0]), Proxy(a,1), currentTime);
            case "set_mod_val_at":
                Count(function,a,3,4); ScheduleSet(Name(a[0]), Proxy(a,3), Num(a[1]), Num(a[2])); return 0d;
            case "tween_mod_val":
                Count(function,a,5,6);
                ScheduleTween(Name(a[0]),Proxy(a,5),currentBeat,Num(a[1]),Name(a[2]),Num(a[3]),Num(a[4])); return 0d;
            case "tween_mod_val_at":
                Count(function,a,6,7);
                ScheduleTween(Name(a[0]),Proxy(a,6),Num(a[1]),Num(a[2]),Name(a[3]),Num(a[4]),Num(a[5])); return 0d;
            case "write_log":
            case "short_msg":
                Count(function,a,1,1);
                if (script.DebugMessages++ < 8)
                    chart.Diagnostics.Add(new(script.Path, 0, "MPF " + function + ": " + Format(a[0])));
                return 0d;
            case "get_judge_cnt":
            {
                Count(function, a, 1, 1);
                int kind = JudgeKind(a[0]); WarnOfflineState(script);
                return game.Judge(kind);
            }
            case "get_combo": case "get_max_combo": case "get_score_ex":
                Count(function, a, 0, 0); WarnOfflineState(script);
                return function == "get_combo" ? game.Combo : function == "get_max_combo" ? game.MaxCombo : game.ExScore;
            case "get_score":
                Count(function, a, 1, 1); WarnOfflineState(script);
                return Num(a[0]) != 0 ? game.MinusScore : game.Score;
            case "add_judge_cnt":
            {
                Count(function, a, 2, 3); int kind = JudgeKind(a[0]); double count = Num(a[1]);
                if (count != Math.Floor(count)) throw new InvalidDataException("ValueError: judge count must be an integer.");
                bool early = a.Length > 2 && Num(a[2]) != 0;
                WarnOfflineState(script); game.Add(kind, count, early); return 0d;
            }
            case "set_judge_timing":
                Count(function, a, 2, 2); game.Timings[JudgeKind(a[0])] = Num(a[1]); WarnOfflineState(script); return 0d;
            case "reset_judge_timing":
                Count(function, a, 0, 0); game.ResetTimings(); WarnOfflineState(script); return 0d;
            case "input_lane_check_pressed": case "input_lane_check_released": case "input_lane_check":
                Count(function, a, 1, 1); InputIndex(a[0], 6); return OfflineInput(script, false);
            case "input_keyboard_check_key":
                Count(function, a, 2, 2); InputIndex(a[0], 2); InputIndex(a[1], 255); return OfflineInput(script, false);
            case "input_check_mouse":
                Count(function, a, 2, 2); InputIndex(a[0], 2); InputIndex(a[1], 3); return OfflineInput(script, false);
            case "mouse_wheel_up": case "mouse_wheel_down": case "get_mouse_pos":
                Count(function, a, 0, 0); return OfflineInput(script, function == "get_mouse_pos");
            case "get_game_var_global":
                Count(function, a, 1, 1); WarnOfflineState(script); return game.Global(Name(a[0]));
            case "get_game_var_instance":
            {
                Count(function, a, 2, 3);
                string instance = Name(a[0]), variable = Name(a[1]);
                if (instance != "cc" || a.Length > 2 && Num(a[2]) != 0)
                    throw new NotSupportedException("MPF offline preview has no instance '" + instance + "'.");
                if (variable.StartsWith("mod_", StringComparison.Ordinal)) return ValueAt(variable[4..], -1, currentTime);
                WarnOfflineState(script);
                return variable switch
                {
                    "currentbeataccurate" => currentBeat, "currentms" => currentTime*1000,
                    "pace_total" => game.AccuracySum, "pace_amount" => game.Count,
                    "sdisp_accscore" => game.Score, "sdisp_exscore" => game.ExScore,
                    "timings" => CopyTimings(script),
                    _ => throw new NotSupportedException("MPF offline preview has no cc variable '"+variable+"'.")
                };
            }
            case "call_callback_mod": case "call_callback_mod_at":
                throw new NotSupportedException(function + " requires original-game live state/callbacks; this MPF preview does not simulate it.");
            default: throw new InvalidDataException("NameError: unknown MPF function '" + function + "'.");
        }
    }

    static int JudgeKind(object? value)
    {
        double kind = Num(value);
        if (Math.Truncate(kind) != kind || kind is < 0 or > 4)
            throw new InvalidDataException("IndexError: judgment type must be 0..4.");
        return (int)kind;
    }
    static void InputIndex(object? value, int max)
    {
        double n = Num(value);
        if (n != Math.Truncate(n) || n < 0 || n > max)
            throw new InvalidDataException("IndexError: input argument out of range.");
    }
    object OfflineInput(Script script, bool mouse)
    {
        if (!inputNotice)
        {
            inputNotice = true;
            chart.Diagnostics.Add(new(script.Path, 0, "MPF input uses an idle offline snapshot: no held/pressed/released keys, no wheel, mouse at (0,0). Live player input is not recorded."));
        }
        if (!mouse) return 0d;
        script.Context.Allocate(2); return new List<object?> { 0d, 0d };
    }
    List<object?> CopyTimings(Script script)
    {
        script.Context.Allocate(5); return game.Timings.Select(x => (object?)x).ToList();
    }

    void Execute(Script script, string section)
    {
        if (script.Dead) return;
        phase = section;
        try { script.Language.Run(script.Context, section); }
        catch (Exception ex) when (ex is InvalidDataException or DivideByZeroException or NotSupportedException or ArgumentException or OverflowException)
        {
            chart.Diagnostics.Add(new(script.Path, 0, "MPF " + script.Name + " / " + section + ": " + ex.Message, true));
            script.Dead = true;
        }
    }

    internal bool HasProxyWrites => writes.Keys.Any(key => key.Proxy >= 0);

    public bool Supports(string name) => scripts.ContainsKey(name);

    public bool TryValue(string name, double time, int proxy, out double value)
    {
        value = 0;
        if (!double.IsFinite(time) || !writes.TryGetValue((name, proxy), out var list)) return false;
        int i = SortedSearch.UpperBound(list, time, static write => write.Time) - 1;
        if (i < 0) return false;
        Write selected = list[i];
        if (!WinsAgainstNative(name, proxy, time, selected)) return false;
        value = selected.Value;
        return true;
    }

    internal static MpfScriptTimeline? Load(Timeline timeline, Chart chart, ViewerProject project)
    {
        if (chart.PerFrame.Count == 0) return null;
        var sourceFiles = MpfScriptSources.Resolve(project);
        string dir = Path.Combine(sourceFiles.Root ?? "", "cmpf");
        foreach (string error in sourceFiles.Errors) chart.Diagnostics.Add(new(dir, 0, error, true));
        var files = sourceFiles.Files;
        if (!chart.PerFrame.Any(frame => files.ContainsKey(frame.Function))) return null;
        var runtime = new MpfScriptTimeline(timeline, chart);
        var ranges = new List<ActiveRange>();
        foreach (var source in chart.PerFrame)
        {
            // CGMK 1.14.1 installs external closures on every object, preserving native names.
            if (timeline.Native?.Data?.PerFrameBindings.ContainsKey(source.Function) == true
                || NativeFunctionTimeline.Supports(timeline.Native?.Data, source.Function)) continue;
            if (!files.TryGetValue(source.Function, out string? file)) continue;
            if (!runtime.scripts.TryGetValue(source.Function, out var script))
            {
                try
                {
                    var info = new FileInfo(file);
                    if (info.Length > 1024 * 1024) throw new InvalidDataException("MPF script exceeds 1 MiB preview safety limit.");
                    var lang = MpfScriptLanguage.Parse(File.ReadAllText(file));
                    script = new(source.Function, file, lang);
                    script.Context.Reset();
                    script.Context.Function = (name, args) => runtime.Invoke(script, name, args);
                    runtime.scripts.Add(source.Function, script);
                    if (lang.RiskLevel is "HIGH_RISK" or "CRITICAL_RISK")
                        chart.Diagnostics.Add(new(file, 0, "MPF declares " + lang.RiskLevel
                            + "; judgment changes affect only the shared offline script state, not real player results."));
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException or FormatException)
                {
                    chart.Diagnostics.Add(new(file, 0, "Invalid MPF " + source.Function + ": " + e.Message, true));
                    continue;
                }
            }
            ranges.Add(new(source, script));
        }
        if (ranges.Count == 0) return null;
        double last = ranges.Max(x => timeline.Bpm.Time(x.Range.EndBeat));
        runtime.ExtendEnd(Math.Max(timeline.End, last + .1));
        try
        {
            for (int tick = 0; tick <= Math.Ceiling(runtime.End * 60); tick++)
            {
                runtime.currentTick = tick;
                runtime.currentTime = tick / 60.0;
                runtime.currentBeat = timeline.Bpm.Beat(runtime.currentTime);
                foreach (var range in ranges)
                {
                    Script script = range.Script;
                    bool inside = runtime.currentBeat > range.Range.StartBeat && runtime.currentBeat < range.Range.EndBeat;
                    if (inside && !range.Active)
                    {
                        range.Active = true;
                        // CGMK mpfRunEntry clears the error latch for each newly entered range.
                        // Cumulative preview budgets remain in force across resets.
                        script.Dead = false;
                        if (script.Language.RefreshOnEachRun)
                        {
                            script.Context.Reset();
                            script.Initialized = false;
                        }
                    }
                    if (inside)
                    {
                        script.Context.Variables["beat"] = runtime.currentBeat;
                        script.Context.Variables["beatStart"] = range.Range.StartBeat;
                        script.Context.Variables["beatEnd"] = range.Range.EndBeat;
                        if (!script.Initialized)
                        {
                            script.Initialized = true;
                            runtime.Execute(script, "init");
                        }
                        runtime.Execute(script, "main");
                    }
                    else if (range.Active)
                    {
                        range.Active = false;
                        script.Context.Variables["beat"] = runtime.currentBeat;
                        script.Context.Variables["beatStart"] = range.Range.StartBeat;
                        script.Context.Variables["beatEnd"] = range.Range.EndBeat;
                        runtime.Execute(script, "finish");
                    }
                }
                runtime.ApplySchedules();
            }
        }
        catch (Exception e) when (e is InvalidDataException or DivideByZeroException or OverflowException)
        {
            chart.Diagnostics.Add(new(dir, 0, "MPF preview stopped: " + e.Message, true));
        }
        return runtime;
    }
}
