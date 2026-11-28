// 在有界离线环境执行 MPF 生命周期，将输出采样为可重放的轨道。

using KenomaGMK.Runtime.Mpf;
using System.Globalization;

namespace KuroakiGimmick.Core;

/// <summary>在固定 60 Hz 时钟上有界执行 Custom Gimmicks 1.14.2 MPF 并保存只读轨道。跳转、快照、并行导出与 Rust 基础轨道求值只读取采样结果。计分与输入使用显式离线基准，脚本间共享判定更改。</summary>
internal sealed class MpfScriptTimeline : IMpfHost
{
    sealed record Write(double Time, double Value, double? TweenRegistered = null);
    sealed class Script(string name, string path, MpfScriptLanguage language)
    {
        internal string Name = name;
        internal string Path = path;
        internal MpfScriptLanguage Language = language;
        internal MpfScriptLanguage.Context Context = new();
        internal int DebugMessages;
        internal bool OfflineStateNotice;
    }
    Timeline timeline;
    readonly Chart chart;
    readonly MpfPreviewGameState game;
    bool inputNotice;
    readonly Dictionary<string, Script> scripts = new(StringComparer.Ordinal);
    readonly Dictionary<(string Name, int Proxy), List<Write>> writes = [];
    readonly MpfModCommands commands;
    readonly MpfSchedule schedule;
    internal double End { get; private set; }
    bool durationNotice;
    double currentTime, currentBeat;
    int writeCount, currentTick;

    MpfScriptTimeline(Timeline timeline, Chart chart)
    {
        this.timeline = timeline;
        this.chart = chart;
        schedule = new(MpfWriteOrder.Registration, 1e-8, true, Easings.Eval, WriteMod);
        commands = new(timeline.Bpm,chart.Proxies,schedule,(name,proxy,value)=>WriteMod(name,proxy,value),ValueAt,true,ExtendEnd);
        game = new(new ScoreState(chart, timeline.Bpm).NoteValue);
    }

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
    static void Count(string name, object?[] args, int min, int max)
    {
        if (args.Length < min || args.Length > max)
            throw new InvalidDataException("TypeError: " + name + " expects " + min + (min == max ? "" : ".." + max) + " arguments.");
    }
    internal MpfScriptTimeline ForkForRender(Timeline owner)
    {
        var copy = (MpfScriptTimeline)MemberwiseClone();
        copy.timeline = owner;
        return copy;
    }

    bool WinsAgainstNative(string name, int proxy, double time, Write write)
    {
        double tick = Math.Floor(time * 60 + 1e-8) / 60;
        double nativeWrite = timeline.NativeTrackWriteTime(name, proxy, tick);
        if (nativeWrite < write.Time - 1e-9) return true;
        // updateMods 先注册 VSM 缓动再执行 MPF，随后按注册顺序运行。同刻时新注册的 MPF 缓动可覆盖已有写入，较早注册的未来缓动不能。
        return write.TweenRegistered is double registered && nativeWrite <= write.Time + 1e-9
            && Math.Ceiling(timeline.NativeTrackStartTime(name, proxy, tick) * 60 - 1e-8) / 60 <= registered + 1e-9;
    }
    double ValueAt(string name, int proxy, double time)
    {
        if (writes.TryGetValue((name, proxy), out var list) && list.Count > 0 && list[^1].Time == currentTime)
            return list[^1].Value; // 本帧更早的直接写入此时已经可见。
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
    void ExtendEnd(double time)
    {
        if (!double.IsFinite(time) || time > 3600)
        {
            if (!durationNotice) chart.Diagnostics.Add(new("cmpf", 0, "MPF preview is limited to 3600 seconds; later scheduled actions are not simulated.", true));
            durationNotice = true;
        }
        End = Math.Max(End, Math.Min(time, 3600));
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
        static string Format(object? arg) =>
            arg is double number ? number.ToString("G", CultureInfo.InvariantCulture) : arg?.ToString() ?? "undefined";
        switch (function)
        {
            case "write_log":
            case "short_msg":
                Count(function, a, 1, 1);
                if (script.DebugMessages++ < 8)
                    chart.Diagnostics.Add(new(script.Path, 0, "MPF " + function + ": " + Format(a[0])));
                return 0d;
            case "get_judge_cnt":
                {
                    Count(function, a, 1, 1);
                    int kind = JudgeKind(a[0]);
                    WarnOfflineState(script);
                    return game.Judge(kind);
                }
            case "get_combo":
            case "get_max_combo":
            case "get_score_ex":
                Count(function, a, 0, 0);
                WarnOfflineState(script);
                return function == "get_combo" ? game.Combo : function == "get_max_combo" ? game.MaxCombo : game.ExScore;
            case "get_score":
                Count(function, a, 1, 1);
                WarnOfflineState(script);
                return Num(a[0]) != 0 ? game.MinusScore : game.Score;
            case "add_judge_cnt":
                {
                    Count(function, a, 2, 3);
                    int kind = JudgeKind(a[0]);
                    double count = Num(a[1]);
                    if (count != Math.Floor(count)) throw new InvalidDataException("ValueError: judge count must be an integer.");
                    bool early = a.Length > 2 && Num(a[2]) != 0;
                    WarnOfflineState(script);
                    game.Add(kind, count, early);
                    return 0d;
                }
            case "set_judge_timing":
                Count(function, a, 2, 2);
                game.Timings[JudgeKind(a[0])] = Num(a[1]);
                WarnOfflineState(script);
                return 0d;
            case "reset_judge_timing":
                Count(function, a, 0, 0);
                game.ResetTimings();
                WarnOfflineState(script);
                return 0d;
            case "input_lane_check_pressed":
            case "input_lane_check_released":
            case "input_lane_check":
                Count(function, a, 1, 1);
                InputIndex(a[0], 6);
                return OfflineInput(script, false);
            case "input_keyboard_check_key":
                Count(function, a, 2, 2);
                InputIndex(a[0], 2);
                InputIndex(a[1], 255);
                return OfflineInput(script, false);
            case "input_check_mouse":
                Count(function, a, 2, 2);
                InputIndex(a[0], 2);
                InputIndex(a[1], 3);
                return OfflineInput(script, false);
            case "mouse_wheel_up":
            case "mouse_wheel_down":
            case "get_mouse_pos":
                Count(function, a, 0, 0);
                return OfflineInput(script, function == "get_mouse_pos");
            case "get_game_var_global":
                Count(function, a, 1, 1);
                WarnOfflineState(script);
                return game.Global(Name(a[0]));
            case "get_game_var_instance":
                {
                    Count(function, a, 3, 3);
                    string instance = Name(a[0]), variable = Name(a[1]);
                    if (instance != "cc" || Num(a[2]) != 0)
                        throw new NotSupportedException("MPF offline preview has no instance '" + instance + "'.");
                    if (variable.StartsWith("mod_", StringComparison.Ordinal)) return ValueAt(variable[4..], -1, currentTime);
                    // 这些值来自谱面状态。原版回调在 MPF 之后执行，速度变化下一 tick 才可见。
                    switch (variable)
                    {
                        case "song_speed": return timeline.MusicPlayback.RateAt((currentTick - 1) / 60.0);
                        case "currentbeataccurate": return currentBeat;
                        case "currentms": return currentTime * 1000;
                        case "cpos": return currentTime;
                    }
                    WarnOfflineState(script);
                    return variable switch
                    {
                        "pace_total" => game.AccuracySum,
                        "pace_amount" => game.Count,
                        "sdisp_accscore" => game.Score,
                        "sdisp_exscore" => game.ExScore,
                        "timings" => CopyTimings(script),
                        _ => throw new NotSupportedException("MPF offline preview has no cc variable '" + variable + "'.")
                    };
                }
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
        script.Context.Allocate(2);
        return new List<object?> { 0d, 0d };
    }
    List<object?> CopyTimings(Script script)
    {
        script.Context.Allocate(5);
        return game.Timings.Select(x => (object?)x).ToList();
    }

    double IMpfHost.BeatAt(double seconds) => timeline.Bpm.Beat(seconds);

    void IMpfHost.ResetSimulation()
    {
        // 本宿主仅在加载时创建一次驱动程序；拖动预览时直接读取采样后的轨道。
        if (currentTick >= 0 && writeCount != 0)
            throw new InvalidOperationException("Build a new MPF timeline to resample.");
        schedule.Reset();
        currentTick = -1;
    }
    object? IMpfHost.Invoke(string name, MpfFrame frame, MpfScriptLanguage.Context context, string function, object?[] arguments)
    {
        currentTick = frame.Tick;
        currentTime = frame.Seconds;
        currentBeat = frame.Beat;
        if(commands.TryInvoke(function,arguments,frame,out double value))return value;
        var script = scripts[name];
        script.Context = context;
        return Invoke(script, function, arguments);
    }
    void IMpfHost.EndTick(MpfFrame frame)
    {
        currentTick = frame.Tick;
        currentTime = frame.Seconds;
        currentBeat = frame.Beat;
        schedule.Advance(currentTime,currentBeat);
    }
    void IMpfHost.ReportError(string name, MpfFrame frame, Exception error) =>
        chart.Diagnostics.Add(new(scripts[name].Path, 0, "MPF " + name + " / " + frame.Phase + ": " + error.Message, true));

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
        // 1. 根据歌曲和难度定位已引用的 MPF 文件，并保留查找诊断。
        if (chart.PerFrame.Count == 0) return null;
        var sourceFiles = MpfScriptSources.Resolve(project);
        string dir = Path.Combine(sourceFiles.Root ?? "", "cmpf");
        foreach (string error in sourceFiles.Errors) chart.Diagnostics.Add(new(dir, 0, error, true));
        var files = sourceFiles.Files;
        if (!chart.PerFrame.Any(frame => files.ContainsKey(frame.Function))) return null;
        var runtime = new MpfScriptTimeline(timeline, chart);
        // 2. 解析匹配程序并建立每段 Init、Main、Finish 的生效区间。
        var ranges = new List<MpfRange>();
        foreach (var source in chart.PerFrame)
        {
            // CGMK 1.14.1 在每个对象上安装外部闭包，同时保留原版名称。
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
            ranges.Add(new(source.Function, source.StartBeat, source.EndBeat));
        }
        if (ranges.Count == 0) return null;
        double last = ranges.Max(x => timeline.Bpm.Time(x.EndBeat));
        runtime.ExtendEnd(Math.Max(timeline.End, last + .1));
        try
        {
            // 3. 按歌曲逻辑时钟推进各区间并记录写入，供之后的随机访问使用。
            var programs = runtime.scripts.ToDictionary(p => p.Key, p => p.Value.Language, StringComparer.Ordinal);
            var playback = new MpfPlayback(programs, ranges, runtime, false, runtime.WarnRandom);
            // 脚本可能延长 End，每次求值后再次检查采样终点。
            for (int tick = 0; tick <= Math.Ceiling(runtime.End * 60); tick++)
                playback.Update(tick / 60d);
        }
        catch (Exception e) when (e is InvalidDataException or DivideByZeroException or OverflowException)
        {
            chart.Diagnostics.Add(new(dir, 0, "MPF preview stopped: " + e.Message, true));
        }
        return runtime;
    }
}
