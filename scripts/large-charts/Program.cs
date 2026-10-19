using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using KuroakiGimmick.Core;
using KuroakiGimmick.Core.Editing;

return Bench.Run(args);

internal static class Bench
{
    sealed record Measurement(string Name, double ElapsedMs, double CpuMs, long AllocatedBytes,
        long ThreadAllocatedBytes, long ManagedBeforeBytes, long ManagedAfterBytes, long RetainedAfterGcBytes,
        long WorkingSetBeforeBytes, long WorkingSetAfterBytes, long? ProcessLifetimePeakWorkingSetBytes,
        int Gen0Collections, int Gen1Collections, int Gen2Collections);
    sealed record EditorChecks(int Lines, int EditableSourceEvents, int RuntimeEvents, int PreservedLines,
        bool Dirty, bool CanUndo, bool CanRedo);
    static readonly List<Measurement> measurements = [];
    static readonly JsonSerializerOptions jsonOptions = new() { WriteIndented = true };

    public static int Run(string[] args)
    {
        string? generated = null;
        bool deleteGenerated = false;
        try
        {
            if (args.Length == 0 || args[0] is "-h" or "--help")
            {
                Console.WriteLine("LargeChartBench <interval|vsm-editor|editor-session|vsm-load|vsc-load|generate-vsm|generate-vsc> "
                    + "[--count 10000] [--operations 1000] [--row-bytes 0] [--input file] [--fixture file] "
                    + "[--engine off|auto|verify|required] [--unlimited] [--label name] [--json file]");
                return 0;
            }
            string kind = args[0];
            if (kind is not ("interval" or "vsm-editor" or "editor-session" or "vsm-load" or "vsc-load" or "generate-vsm" or "generate-vsc"))
                throw new ArgumentException("Unknown benchmark case: " + kind);
            var options = new Dictionary<string, string>(StringComparer.Ordinal);
            bool unlimited = false;
            for (int i = 1; i < args.Length; i++)
            {
                if (args[i] == "--unlimited") { unlimited = true; continue; }
                if (args[i] is not ("--count" or "--operations" or "--row-bytes" or "--input" or "--fixture" or "--engine" or "--label" or "--json") || i + 1 >= args.Length)
                    throw new ArgumentException("Unknown option or missing value: " + args[i]);
                options.Add(args[i], args[++i]);
            }
            int count = Positive(options.GetValueOrDefault("--count", "10000"), "count");
            int operations = Positive(options.GetValueOrDefault("--operations", "1000"), "operations");
            if (!int.TryParse(options.GetValueOrDefault("--row-bytes", "0"), NumberStyles.None, CultureInfo.InvariantCulture, out int rowBytes)
                || rowBytes < 0 || rowBytes > 16 * 1024 * 1024)
                throw new ArgumentException("row-bytes must be between zero and 16 MiB.");
            string engine = options.GetValueOrDefault("--engine", "off");
            if (engine is not ("off" or "auto" or "verify" or "required")) throw new ArgumentException("Invalid engine mode.");
            Environment.SetEnvironmentVariable("KG_RUST_ENGINE", engine);
            // Enter an explicit internal load-limit scope without
            // reading/writing the invoking user's persisted application preferences.
            // All timed operations below use public APIs present in both revisions.
            var overrideMethod = typeof(ChartLoadLimits).GetMethod("Override", BindingFlags.Static | BindingFlags.NonPublic)
                ?? throw new MissingMethodException("This repository does not expose the benchmark load-limit scope.");
            using var scope = (IDisposable)overrideMethod.Invoke(null, [unlimited])!;
            if (kind == "editor-session") ConfigureSessionDefaults(unlimited);
            Warmup();
            string? input = options.GetValueOrDefault("--input");
            if (input != null && options.ContainsKey("--fixture")) throw new ArgumentException("Use --input or --fixture, not both.");
            if (kind != "interval" && input == null)
            {
                bool vsc = kind is "vsc-load" or "generate-vsc";
                generated = options.GetValueOrDefault("--fixture") ?? Path.Combine(Path.GetTempPath(), "kg-large-chart-" + Guid.NewGuid().ToString("N") + (vsc ? ".vsc" : ".vsm"));
                if (kind.StartsWith("generate-", StringComparison.Ordinal) && !options.ContainsKey("--fixture"))
                    throw new ArgumentException("Generation-only cases require --fixture so the output location is explicit.");
                generated = Path.GetFullPath(generated);
                deleteGenerated = !options.ContainsKey("--fixture");
                Generate(generated, count, vsc, rowBytes);
                input = generated;
            }
            else if (input != null) input = Path.GetFullPath(input);
            if (kind == "interval" && input != null) throw new ArgumentException("The interval case uses generated intervals, not a file.");
            long? inputBytes = input == null ? null : new FileInfo(input).Length;
            long records = count, matched = 0, diagnostics = 0;
            EditorChecks? editorChecks = null;
            string checksum;
            switch (kind)
            {
                case "interval":
                {
                    var index = Measure("construct", () => new IntervalIndex<int>(Intervals(count)));
                    var queryResult = Measure("query", () => Query(index, count, operations));
                    matched = queryResult.Count;
                    checksum = queryResult.Hash.ToString("X16", CultureInfo.InvariantCulture);
                    GC.KeepAlive(index);
                    break;
                }
                case "vsm-editor":
                {
                    var document = Measure("load", () => VsmDocument.Load(input!));
                    var clips = document.Clips.ToArray();
                    records = clips.Length;
                    if (clips.Length == 0) throw new InvalidDataException("The editor fixture has no editable clips.");
                    Measure("lookup-first", () => document.SourceLine(clips[^1].Id));
                    var lookup = Measure("lookup-warm", () => Lookup(document, clips, operations));
                    matched = lookup;
                    int replacements = Math.Min(operations, clips.Length);
                    Measure("replace", () =>
                    {
                        for (int i = 0; i < replacements; i++)
                        {
                            int position = (int)((long)i * clips.Length / replacements);
                            document.Replace(clips[position] with { To = "2" });
                        }
                        return replacements;
                    });
                    var hash = new StableHash();
                    foreach (var line in document.Lines) { hash.Add(line.Text); hash.Add(line.Ending); }
                    checksum = hash.Value.ToString("X16", CultureInfo.InvariantCulture);
                    GC.KeepAlive(document);
                    break;
                }
                case "vsm-load":
                {
                    var chart = Measure("load", () => { var c = new Chart(); VsmReader.ReplaceMods(c, input!); return c; });
                    records = chart.Mods.Count; diagnostics = chart.Diagnostics.Count;
                    var hash = new StableHash();
                    foreach (var mod in chart.Mods)
                    {
                        hash.Add(mod.Beat); hash.Add(mod.Duration); hash.Add(mod.Ease); hash.Add(mod.From); hash.Add(mod.To);
                        hash.Add(mod.Name); hash.Add(mod.Proxy); hash.Add(mod.Order); hash.Add(mod.SourceLine);
                    }
                    foreach (var frame in chart.PerFrame) { hash.Add(frame.StartBeat); hash.Add(frame.EndBeat); hash.Add(frame.Function); }
                    HashMetadata(hash, chart);
                    checksum = hash.Value.ToString("X16", CultureInfo.InvariantCulture);
                    GC.KeepAlive(chart);
                    break;
                }
                case "editor-session":
                {
                    var project = new ViewerProject
                    {
                        Gimmick = input, Profile = "core", GameUiEnabled = false,
                        Audio = null, Notes = false, PostProcessing = false
                    };
                    var session = Measure("session-load", () => new Session(project));
                    var editor = Measure("editor-open", () => new EditorDocument(session));
                    editorChecks = Measure("editor-checks", () => new EditorChecks(editor.Vsm.Lines.Count,
                        editor.Vsm.Clips.Count(), session.Chart.Mods.Count, editor.Vsm.PreservedLineCount,
                        editor.Dirty, editor.CanUndo, editor.CanRedo));
                    if (editorChecks.Dirty || editorChecks.CanUndo || editorChecks.CanRedo)
                        throw new InvalidDataException("A freshly opened editor must be clean with empty undo/redo history.");
                    records = editorChecks.EditableSourceEvents;
                    diagnostics = session.Chart.Diagnostics.Count;
                    var hash = new StableHash();
                    // Inspect the actual source partitions, never Vsm.Text, which
                    // would itself allocate another whole-file string in the test.
                    foreach (var line in editor.Vsm.Lines) { hash.Add(line.Text); hash.Add(line.Ending); }
                    hash.Add(editorChecks.Lines); hash.Add(editorChecks.EditableSourceEvents);
                    hash.Add(editorChecks.RuntimeEvents); hash.Add(editorChecks.PreservedLines);
                    HashMetadata(hash, session.Chart);
                    checksum = hash.Value.ToString("X16", CultureInfo.InvariantCulture);
                    GC.KeepAlive(editor); GC.KeepAlive(session);
                    break;
                }
                case "vsc-load":
                {
                    var chart = Measure("load", () => VscReader.Load(input!));
                    records = chart.Notes.Count; diagnostics = chart.Diagnostics.Count;
                    var hash = new StableHash();
                    hash.Add((int)chart.VscDialect);
                    foreach (var note in chart.Notes)
                    {
                        hash.Add(note.Time); hash.Add(note.End); hash.Add(note.Type); hash.Add(note.Lane);
                        foreach (var pair in note.Extra.OrderBy(pair => pair.Key))
                        { hash.Add(pair.Key); hash.Add(Convert.ToString(pair.Value, CultureInfo.InvariantCulture) ?? ""); }
                        if (note.ModExtra != null)
                            foreach (var pair in note.ModExtra.OrderBy(pair => pair.Key, StringComparer.Ordinal))
                            { hash.Add(pair.Key); hash.Add(pair.Value ?? "<null>"); }
                    }
                    HashMetadata(hash, chart);
                    checksum = hash.Value.ToString("X16", CultureInfo.InvariantCulture);
                    GC.KeepAlive(chart);
                    break;
                }
                default:
                {
                    using var stream = File.OpenRead(input!);
                    checksum = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(stream));
                    break;
                }
            }
            var report = new
            {
                SchemaVersion = 1, Case = kind, Label = options.GetValueOrDefault("--label"),
                Runtime = RuntimeInformation.FrameworkDescription, OS = RuntimeInformation.OSDescription,
                Architecture = RuntimeInformation.ProcessArchitecture.ToString(),
                ApplicationVersion = typeof(Chart).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion,
                Engine = engine, Unlimited = unlimited,
                RequestedRecords = generated != null || kind == "interval" ? (int?)count : null, Operations = operations,
                RowMinimumBytes = rowBytes, PaddingDeclaredForInput = generated == null && rowBytes > 0,
                Workload = kind == "interval" ? "synthetic overlapping intervals" : rowBytes > 0 ? "synthetic padded event rows"
                    : generated != null ? "synthetic unpadded event rows" : "specified input",
                Input = input, InputBytes = inputBytes, GeneratedFixture = generated != null, FixtureDeletedAfterRun = deleteGenerated,
                Records = records, MatchedOrLookupSum = matched, Diagnostics = diagnostics, Checksum = checksum,
                EditorSession = editorChecks,
                Measurements = measurements
            };
            if (generated != null && !kind.StartsWith("generate-", StringComparison.Ordinal) && records != count)
                throw new InvalidDataException($"Generated fixture contains {count} records but loader returned {records}.");
            string json = JsonSerializer.Serialize(report, jsonOptions);
            if (options.TryGetValue("--json", out string? output))
            {
                string full = Path.GetFullPath(output);
                Directory.CreateDirectory(Path.GetDirectoryName(full)!);
                File.WriteAllText(full, json + Environment.NewLine);
            }
            Console.WriteLine(json);
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(JsonSerializer.Serialize(new { Error = ex.GetType().Name, ex.Message }, jsonOptions));
            return 1;
        }
        finally { if (deleteGenerated && generated != null) File.Delete(generated); }
    }

    static int Positive(string value, string name) => int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int number) && number > 0
        ? number : throw new ArgumentException(name + " must be an integer greater than zero.");

    static void ConfigureSessionDefaults(bool unlimited)
    {
        // Session evaluates other engine preferences even with no audio. Install
        // explicit in-memory defaults through the existing settings hook so this
        // benchmark never reads or writes the invoking user's preferences.
        var settingsType = typeof(Session).Assembly.GetType("KuroakiGimmick.Core.OptimizationEngineSettings")
            ?? throw new MissingMemberException("Session settings type is unavailable.");
        var apply = settingsType.GetMethod("Apply", BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new MissingMethodException("Session settings hook is unavailable.");
        apply.Invoke(null, [new ViewerSettings
        {
            UnlimitedChartSize = unlimited, ParallelLoading = false, DecodedAudioCache = false,
            CalculationEngine = "compatibility", AudioEngine = "nvorbis"
        }]);
    }

    static void Generate(string path, int count, bool vsc, int rowBytes)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        // Refuse to clobber a real chart or a previously generated fixture.
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read, 1024 * 1024);
        using var writer = new StreamWriter(stream, new UTF8Encoding(false), 1024 * 1024) { NewLine = "\n" };
        if (!vsc) writer.WriteLine("!obj:obj_base_gimmick");
        string padding = rowBytes > 0 ? new string(' ', rowBytes) : "";
        for (int i = 0; i < count; i++)
        {
            string row;
            if (vsc)
            {
                long time = (long)(i / 4) * 10;
                row = i % 16 == 0
                    ? FormattableString.Invariant($"{time},2,{i % 4},{time + 1000}")
                    : FormattableString.Invariant($"{time},0,{i % 4}");
            }
            else row = FormattableString.Invariant($"{i / 4},1,linear,0,1,prx,0");
            writer.Write(row);
            // Padding is whitespace on real records, not opaque comment-only data.
            // rowBytes includes the final LF; shorter requested sizes never truncate.
            if (rowBytes > row.Length + 1) writer.Write(padding.AsSpan(0, rowBytes - row.Length - 1));
            writer.WriteLine();
        }
        // Legal late header catches accidental O(N) proxy-header scans in bulk Replace.
        if (!vsc) writer.WriteLine("!proxies:1");
    }

    static IEnumerable<IntervalIndex<int>.Entry> Intervals(int count)
    {
        for (int i = 0; i < count; i++)
        {
            double start = i / 4;
            yield return new(i, start, start + (i % 64 == 0 ? 4096 : 1), i);
        }
    }

    static (long Count, ulong Hash) Query(IntervalIndex<int> index, int count, int operations)
    {
        var hash = new StableHash(); long matches = 0;
        for (int i = 0; i < operations; i++)
        {
            double start = (long)i * 104729 % Math.Max(1, count / 4);
            foreach (var entry in index.Overlap(start, start + 8))
            { matches++; hash.Add(entry.Value); hash.Add(entry.Start); hash.Add(entry.End); }
        }
        return (matches, hash.Value);
    }

    static long Lookup(VsmDocument document, VsmDocument.Clip[] clips, int operations)
    {
        long result = 0;
        for (int i = 0; i < operations; i++)
        {
            int position = (int)((long)i * 104729 % clips.Length);
            var clip = document.Find(clips[position].Id) ?? throw new InvalidDataException("Lookup dropped a source clip.");
            result += document.SourceLine(clip.Id) + document.PreservedLineCount;
        }
        return result;
    }

    static void HashMetadata(StableHash hash, Chart chart)
    {
        hash.Add(chart.ObjectName); hash.Add(chart.Proxies); hash.Add(chart.ProxyCountDeclared ? 1 : 0);
        foreach (var pair in chart.Metadata.OrderBy(pair => pair.Key, StringComparer.Ordinal)) { hash.Add(pair.Key); hash.Add(pair.Value); }
        foreach (var item in chart.Diagnostics) { hash.Add(item.Line); hash.Add(item.Message); hash.Add(item.Error ? 1 : 0); }
    }

    static T Measure<T>(string name, Func<T> action)
    {
        Collect();
        using var process = Process.GetCurrentProcess(); process.Refresh();
        long managedBefore = GC.GetTotalMemory(false), workingBefore = process.WorkingSet64;
        long allocated = GC.GetTotalAllocatedBytes(precise: true), threadAllocated = GC.GetAllocatedBytesForCurrentThread();
        int gen0 = GC.CollectionCount(0), gen1 = GC.CollectionCount(1), gen2 = GC.CollectionCount(2);
        TimeSpan cpu = process.TotalProcessorTime;
        long start = Stopwatch.GetTimestamp();
        T result = action();
        double elapsedMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        long threadBytes = GC.GetAllocatedBytesForCurrentThread() - threadAllocated;
        long totalBytes = GC.GetTotalAllocatedBytes(precise: true) - allocated;
        long managedAfter = GC.GetTotalMemory(false);
        gen0 = GC.CollectionCount(0) - gen0; gen1 = GC.CollectionCount(1) - gen1; gen2 = GC.CollectionCount(2) - gen2;
        process.Refresh();
        double cpuMs = (process.TotalProcessorTime - cpu).TotalMilliseconds;
        long workingAfter = process.WorkingSet64, peak = process.PeakWorkingSet64;
        Collect();
        long retained = GC.GetTotalMemory(false);
        GC.KeepAlive(result);
        measurements.Add(new(name, elapsedMs, cpuMs, totalBytes, threadBytes, managedBefore, managedAfter, retained,
            workingBefore, workingAfter, peak > 0 ? peak : null, gen0, gen1, gen2));
        return result;
    }

    static void Collect() { GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); }

    static void Warmup()
    {
        var document = VsmDocument.FromText("0,1,linear,0,1,prx,-1\n");
        var clip = document.Clips.Single();
        _ = document.Find(clip.Id); _ = document.SourceLine(clip.Id); _ = document.PreservedLineCount;
        document.Replace(clip with { To = "2" });
        _ = Query(new(Intervals(128)), 128, 8);
    }

    // FNV-1a over explicit little-endian numeric bits / UTF-16 characters. IDs are
    // intentionally excluded because VsmDocument generates fresh GUIDs per parse.
    sealed class StableHash
    {
        public ulong Value { get; private set; } = 14695981039346656037;
        void Byte(byte value) { unchecked { Value = (Value ^ value) * 1099511628211; } }
        public void Add(int value) => Add((double)value);
        public void Add(double value)
        {
            ulong bits = unchecked((ulong)BitConverter.DoubleToInt64Bits(value));
            for (int shift = 0; shift < 64; shift += 8) Byte((byte)(bits >> shift));
        }
        public void Add(string value)
        {
            foreach (char character in value) { Byte((byte)character); Byte((byte)(character >> 8)); }
            Byte(0xff); Byte(0xff);
        }
    }
}
