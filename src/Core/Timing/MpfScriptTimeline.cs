using System.Globalization;

namespace KuroakiGimmick.Core;

/// <summary>
/// Deterministic, bounded Custom Gimmicks 1.14 MPF preview. Programs run on the
/// original 60 Hz song clock into immutable sample tracks; seeking, snapshots,
/// parallel export and Rust base-track evaluation cannot advance script state.
/// Gameplay mutation and original-engine callbacks are NOT simulated.
/// </summary>
internal sealed class MpfScriptTimeline
{
    sealed record Write(double Time, double Value);
    sealed record DeferredSet(double Beat, string Name, int Proxy, double Value);
    sealed record DeferredTween(double Start, double End, string Ease, string Name, int Proxy, double From, double To);

    sealed class Script(string name, string path, MpfScriptLanguage language, uint seed)
    {
        internal string Name = name;
        internal string Path = path;
        internal MpfScriptLanguage Language = language;
        internal MpfScriptLanguage.Context Context = new();
        internal Random Random = new(unchecked((int)seed));
        internal bool Initialized;
        internal bool Dead;
        internal bool SeedChanged;
        internal int DebugMessages;
    }
    sealed class ActiveRange(FrameEvent range, Script script)
    {
        internal FrameEvent Range = range;
        internal Script Script = script;
        internal bool Active;
    }

    readonly Timeline timeline;
    readonly Chart chart;
    readonly Dictionary<string, Script> scripts = new(StringComparer.Ordinal);
    readonly Dictionary<(string Name, int Proxy), List<Write>> writes = [];
    readonly List<DeferredSet> pendingSets = [];
    readonly List<DeferredTween> pendingTweens = [];
    double currentTime, currentBeat;
    string phase = "main";
    int writeCount, scheduledCount;

    MpfScriptTimeline(Timeline timeline, Chart chart)
    {
        this.timeline = timeline;
        this.chart = chart;
    }

    static uint StableSeed(string name)
    {
        uint hash = 2166136261;
        foreach (char c in name) hash = unchecked((hash ^ c) * 16777619);
        return hash;
    }
    static double Num(object? value) => value is double d && double.IsFinite(d)
        ? d : throw new InvalidDataException("TypeError: numeric argument required.");
    static string Name(object? value) => value is string s && s.Length is > 0 and <= 128
        ? s : throw new InvalidDataException("TypeError: nonempty string argument required.");
    static int Proxy(object?[] args, int index)
    {
        if (index >= args.Length) return -1;
        double raw = Num(args[index]);
        if (Math.Truncate(raw) != raw || raw is < -1 or > 63)
            throw new InvalidDataException("IndexError: proxy must be -1..63.");
        return (int)raw;
    }
    static void Count(string name, object?[] args, int min, int max)
    {
        if (args.Length < min || args.Length > max)
            throw new InvalidDataException("TypeError: " + name + " expects " + min + (min == max ? "" : ".." + max) + " arguments.");
    }
    double ValueAt(string name, int proxy, double time)
    {
        if (writes.TryGetValue((name, proxy), out var list))
        {
            int index = SortedSearch.UpperBound(list, time, static w => w.Time) - 1;
            if (index >= 0) return list[index].Value;
        }
        return timeline.NativeRawValue(name, time, proxy);
    }
    void WriteMod(string name, int proxy, double value)
    {
        if (!double.IsFinite(value)) throw new InvalidDataException("ValueError: non-finite MPF mod value.");
        if (++writeCount > 1_000_000) throw new InvalidDataException("MaxLoopReached: MPF preview output exceeds one million writes.");
        var key = (name, proxy);
        if (!writes.TryGetValue(key, out var track)) writes[key] = track = [];
        if (track.Count > 0 && track[^1].Time == currentTime) track[^1] = new(currentTime, value);
        else track.Add(new(currentTime, value));
    }
    void ScheduleSet(string name, int proxy, double beat, double value)
    {
        if (!double.IsFinite(beat) || !double.IsFinite(value)) throw new InvalidDataException("ValueError: invalid scheduled MPF value.");
        if (++scheduledCount > 100_000) throw new InvalidDataException("MaxLoopReached: too many scheduled MPF actions.");
        pendingSets.Add(new(beat, name, proxy, value));
    }
    void ScheduleTween(string name, int proxy, double startBeat, double durationBeats, string ease, double from, double to)
    {
        if (!double.IsFinite(startBeat) || !double.IsFinite(durationBeats) || !double.IsFinite(from) || !double.IsFinite(to))
            throw new InvalidDataException("ValueError: invalid MPF tween.");
        if (!Easings.IsKnown(ease)) throw new InvalidDataException("NameError: unknown easing '" + ease + "'.");
        if (++scheduledCount > 100_000) throw new InvalidDataException("MaxLoopReached: too many MPF tweens.");
        double start = timeline.Bpm.Time(startBeat);
        double end = start + Math.Max(0, durationBeats) * 60 / timeline.Bpm.BpmAtBeat(startBeat);
        pendingTweens.Add(new(start, end, ease, name, proxy, from, to));
    }
    void ApplySchedules()
    {
        for (int i = 0; i < pendingSets.Count;)
        {
            if (currentBeat + 1e-8 < pendingSets[i].Beat) { i++; continue; }
            var set = pendingSets[i];
            WriteMod(set.Name, set.Proxy, set.Value);
            pendingSets.RemoveAt(i);
        }
        for (int i = 0; i < pendingTweens.Count;)
        {
            var tween = pendingTweens[i];
            if (currentTime + 1e-8 < tween.Start) { i++; continue; }
            double progress = tween.End <= tween.Start ? 1 : Math.Clamp((currentTime - tween.Start) / (tween.End - tween.Start), 0, 1);
            WriteMod(tween.Name, tween.Proxy, tween.From + (tween.To - tween.From) * Easings.Eval(tween.Ease, progress));
            if (progress >= 1) pendingTweens.RemoveAt(i);
            else i++;
        }
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
            case "str_cat": Count(function,a,2,2); return Format(a[0]) + Format(a[1]);
            case "array_create":
            {
                Count(function,a,1,1); double count = Num(a[0]);
                if (count < 0 || count > 100_000 || Math.Truncate(count) != count) throw new InvalidDataException("IndexError: array size is out of range.");
                return Enumerable.Repeat<object?>(0d, (int)count).ToList();
            }
            case "array_length": Count(function,a,1,1); return (double)ArrayArg(a,0).Count;
            case "array_contains": Count(function,a,2,2); return ArrayArg(a,0).Contains(a[1]) ? 1d : 0d;
            case "array_push":
            {
                Count(function,a,2,2); var array = ArrayArg(a,0);
                if (array.Count >= 100_000) throw new InvalidDataException("IndexError: array exceeds 100000 elements.");
                array.Add(a[1]); return array;
            }
            case "array_insert":
            {
                Count(function,a,3,3); var array = ArrayArg(a,0);
                double raw = Num(a[2]);
                if (raw < 0 || raw > array.Count || Math.Truncate(raw) != raw || array.Count >= 100_000)
                    throw new InvalidDataException("IndexError: cannot insert into array.");
                array.Insert((int)raw, a[1]); return array;
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
            case "random": Count(function,a,1,1); return script.Random.NextDouble() * Num(a[0]);
            case "irandom":
            {
                Count(function,a,1,1); double upper = Num(a[0]);
                if (upper < 0 || upper > int.MaxValue - 1 || Math.Truncate(upper) != upper)
                    throw new InvalidDataException("ValueError: invalid irandom upper bound.");
                return (double)script.Random.Next((int)upper + 1);
            }
            case "random_set_seed":
                Count(function,a,1,1); script.Random = new(unchecked((int)Num(a[0]))); script.SeedChanged = true; return 0d;
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
            // The original game mutates live judgment/score and dispatches instance
            // callbacks. Pretending the editor's autoplay has the same state
            // would silently produce incorrect previews.
            case "get_judge_cnt": case "get_combo": case "get_max_combo":
            case "add_judge_cnt": case "set_judge_timing": case "reset_judge_timing":
            case "call_callback_mod": case "call_callback_mod_at":
                throw new NotSupportedException(function + " requires original-game live state/callbacks; this MPF preview does not simulate it.");
            default: throw new InvalidDataException("NameError: unknown MPF function '" + function + "'.");
        }
    }

    void Execute(Script script, string section)
    {
        if (script.Dead) return;
        phase = section;
        try { script.Language.Run(script.Context, section); }
        catch (Exception ex) when (ex is InvalidDataException or DivideByZeroException or NotSupportedException or ArgumentException)
        {
            chart.Diagnostics.Add(new(script.Path, 0, "MPF " + script.Name + " / " + section + ": " + ex.Message, true));
            script.Dead = true;
        }
    }

    public bool Supports(string name) => scripts.ContainsKey(name);

    public bool TryValue(string name, double time, int proxy, out double value)
    {
        value = 0;
        if (!double.IsFinite(time) || !writes.TryGetValue((name, proxy), out var list)) return false;
        int i = SortedSearch.UpperBound(list, time, static write => write.Time) - 1;
        if (i < 0) return false;
        Write selected = list[i];
        // Source updateMods runs scripts before applying chart tweens. A newer
        // VSM tween/write wins; a subsequent MPF tick can take ownership back.
        if (timeline.NativeTrackWriteTime(name, proxy, time) >= selected.Time - 1e-9) return false;
        value = selected.Value;
        return true;
    }

    internal static MpfScriptTimeline? Load(Timeline timeline, Chart chart, ViewerProject project)
    {
        if (chart.PerFrame.Count == 0) return null;
        string? root = SongFiles.Root(project);
        if (root == null) return null;
        string dir = Path.Combine(root, "cmpf");
        if (!Directory.Exists(dir)) return null;
        var runtime = new MpfScriptTimeline(timeline, chart);
        if (chart.ObjectName != "obj_custom_gimmick")
        {
            chart.Diagnostics.Add(new(dir, 0, "Custom Gimmicks 1.14 MPF requires !obj:obj_custom_gimmick.", true));
            return null;
        }
        string difficulty = SongFiles.Difficulty(project.Chart ?? project.Gimmick ?? "").ToUpperInvariant();
        // GLOBAL first; difficulty-specific programs override only same-name globals.
        var files = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string file in Directory.EnumerateFiles(dir).Order(StringComparer.Ordinal))
        {
            string leaf = Path.GetFileName(file);
            if (!leaf.EndsWith(".vsmpf", StringComparison.OrdinalIgnoreCase)) continue;
            string stem = leaf[..^6];
            int dot = stem.IndexOf('.');
            if (dot < 1 || dot == stem.Length - 1) continue;
            string selector = stem[..dot].ToUpperInvariant(), name = stem[(dot + 1)..];
            if (selector == "GLOBAL") files.TryAdd(name, file);
        }
        foreach (string file in Directory.EnumerateFiles(dir).Order(StringComparer.Ordinal))
        {
            string leaf = Path.GetFileName(file);
            if (!leaf.EndsWith(".vsmpf", StringComparison.OrdinalIgnoreCase)) continue;
            string stem = leaf[..^6];
            int dot = stem.IndexOf('.');
            if (dot < 1 || dot == stem.Length - 1) continue;
            if (stem[..dot].Equals(difficulty, StringComparison.OrdinalIgnoreCase))
                files[stem[(dot + 1)..]] = file;
        }
        var ranges = new List<ActiveRange>();
        foreach (var source in chart.PerFrame)
        {
            if (!files.TryGetValue(source.Function, out string? file)) continue;
            if (!runtime.scripts.TryGetValue(source.Function, out var script))
            {
                try
                {
                    var info = new FileInfo(file);
                    if (info.Length > 1024 * 1024) throw new InvalidDataException("MPF script exceeds 1 MiB preview safety limit.");
                    var lang = MpfScriptLanguage.Parse(File.ReadAllText(file));
                    script = new(source.Function, file, lang, StableSeed(source.Function));
                    script.Context.Reset();
                    script.Context.Function = (name, args) => runtime.Invoke(script, name, args);
                    runtime.scripts.Add(source.Function, script);
                    if (lang.RiskLevel is "HIGH_RISK" or "CRITICAL_RISK")
                        chart.Diagnostics.Add(new(file, 0, "MPF declares " + lang.RiskLevel
                            + "; judgment mutation cannot be simulated in K/G and such calls are rejected.", true));
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
        double duration = Math.Clamp(Math.Max(timeline.End, last + .1), 0, 3600);
        int ticks = (int)Math.Ceiling(duration * 60);
        try
        {
            for (int tick = 0; tick <= ticks; tick++)
            {
                runtime.currentTime = tick / 60.0;
                runtime.currentBeat = timeline.Bpm.Beat(runtime.currentTime);
                foreach (var range in ranges)
                {
                    Script script = range.Script;
                    bool inside = runtime.currentBeat > range.Range.StartBeat && runtime.currentBeat < range.Range.EndBeat;
                    if (inside && !range.Active)
                    {
                        range.Active = true;
                        script.Dead = false;
                        if (script.Language.RefreshOnEachRun)
                        {
                            script.Context.Reset();
                            script.Random = new(unchecked((int)StableSeed(script.Name)));
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
