using KuroakiGimmick.Core;

static class EngineCompatibility
{
    static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    internal static void Run(bool bpmScale)
    {
        string? previous = Environment.GetEnvironmentVariable("KG_RUST_ENGINE");
        string directory = Path.Combine(Path.GetTempPath(), "kg-engine-regression-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            const string vsm = "!obj:obj_custom_gimmick // object\n!proxies:2 // count\n0,0,linear,0,1,prx,-1 // event\nmpf // marker\n0,4,test_callback // function\n";
            Diagnostic? expected = null;
            foreach (string mode in new[] { "off", "auto", "verify" })
            {
                Environment.SetEnvironmentVariable("KG_RUST_ENGINE", mode);
                foreach (string text in new[] { vsm, vsm.Replace("\n", "\r\n"), vsm.Replace("\n", "\r") })
                {
                    var chart = new Chart();
                    chart.Diagnostics.Add(new("chart.vsc", 2, "Existing chart warning"));
                    VsmReader.ReplaceModsText(chart, text, "comments.vsm");
                    Check(chart.ObjectName == "obj_custom_gimmick" && chart.Proxies == 2 && chart.Mods.Count == 1,
                        $"{mode}: trailing comment changed metadata/event");
                    Check(chart.Mods[0].SourceLine == 3 && chart.PerFrame.Single().Function == "test_callback",
                        $"{mode}: trailing comment changed source lines/function");
                    Check(chart.Diagnostics.Count == 2 && !chart.Diagnostics.Any(d => d.Error),
                        $"{mode}: comments must preserve existing diagnostics and add a warning");
                    var warning = chart.Diagnostics[1];
                    Check(warning.Source == "comments.vsm" && warning.Line == 1, "Comment source location changed");
                    expected ??= warning;
                    Check(warning == expected, "Comment warning differs between engines/newline formats");
                }
                string file = Path.Combine(directory, "comments.vsm");
                File.WriteAllText(file, vsm);
                var fromFile = new Chart();
                VsmReader.ReplaceMods(fromFile, file);
                Check(fromFile.Diagnostics.Single() == (expected! with { Source = file }), "File comment warning changed");

                foreach (string extension in new[] { "vsc", "vsb" })
                {
                    string oversized = Path.Combine(directory, "oversized." + extension);
                    using (var stream = File.Create(oversized)) stream.SetLength(128L * 1024 * 1024 + 1);
                    long allocated = GC.GetAllocatedBytesForCurrentThread();
                    try
                    {
                        _ = extension == "vsc" ? VscReader.Load(oversized) : VsbReader.Load(oversized);
                        throw new Exception("Oversized chart was accepted");
                    }
                    catch (InvalidDataException) { }
                    Check(GC.GetAllocatedBytesForCurrentThread() - allocated < 4 * 1024 * 1024,
                        $"{mode}: oversized {extension} was read before size validation");
                }
            }
            Console.WriteLine("PASS engine compatibility: trailing-comment warnings, source locations, newline formats and pre-read file limits");
        }
        finally
        {
            Environment.SetEnvironmentVariable("KG_RUST_ENGINE", previous);
            Directory.Delete(directory, recursive: true);
        }
        if (!bpmScale) return;

        const int count = 2_000_001;
        var large = new Chart();
        var extra = new Dictionary<int, object> { [1] = 120.0 };
        for (int i = 0; i < count; i++) large.Notes.Add(new(i + 1, 3, 0, i + 1, extra));
        var map = new BpmMap(large, 120, 0);
        Check(map.Segments.Count == count + 1 && map.Segments[^1].Time == count && map.Time(count * 2.0) == count,
            "BPM scale regression failed above the historical count limit");
        Console.WriteLine("PASS BPM scale: 2,000,001 changes / 2,000,002 segments");
    }
}
