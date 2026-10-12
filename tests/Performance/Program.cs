using System.Diagnostics;
using System.Text.Json;
using KuroakiGimmick.Core;
using KuroakiGimmick.Graphics;
using KuroakiGimmick.Native;

System.Globalization.CultureInfo.DefaultThreadCurrentCulture = System.Globalization.CultureInfo.InvariantCulture;
System.Globalization.CultureInfo.DefaultThreadCurrentUICulture = System.Globalization.CultureInfo.InvariantCulture;

#if !BASELINE
if (args.FirstOrDefault() == "switch-memory")
{
    PerformanceStats.Enabled = true;
    SwitchMemory.Run(args[1], args.Length > 2 ? int.Parse(args[2]) : 30);
    return;
}
#endif
int count = args.Length > 1 ? int.Parse(args[1]) : 10000;
int iterations = args.Length > 2 ? int.Parse(args[2]) : 12;
string mode = args.FirstOrDefault() ?? "skin";
#if !BASELINE
PerformanceStats.Enabled = true;
#endif
static void Check(bool ok, string text) { if (!ok) throw new Exception(text); }
static void Report(string label, double[] values)
{
    Array.Sort(values);
    double P(double q) => values[(int)Math.Ceiling(q * values.Length) - 1];
    Console.WriteLine($"{label}: samples={values.Length} mean={values.Average():F3}ms P50={P(.5):F3}ms P95={P(.95):F3}ms P99={P(.99):F3}ms max={values[^1]:F3}ms throughput={1000 / values.Average():F2}/s");
}
if (mode == "audio")
{
    Sdl.SDL_SetMainReady();
    using var transport = new Transport();
    var audio = new AudioData(new float[48000 * 2 * 12], 48000, 2);
    transport.Load(audio, audio.Duration); Check(transport.AudioError == null, transport.AudioError ?? "");
    transport.SetPlaying(true);
    // Deliberately do not call UI Update for three seconds.
    Thread.Sleep(3000);
#if !BASELINE
    Check(transport.FeedCycles > 100, "Audio supply remained tied to UI"); Check(transport.Underruns == 0, "Audio queue starved during UI stall");
    Console.WriteLine($"audio UI stall=3000ms supplyCycles={transport.FeedCycles} observedUnderruns={transport.Underruns}");
#endif
    Check(transport.Position > 2.8 && transport.Position < 3.3, "Clock drift during UI stall");
#if !BASELINE
    long beforeSeeks = transport.Underruns;
    for (int i = 0; i < 20; i++) transport.Seek(1 + i * .01);
    Check(transport.Underruns == beforeSeeks, "Intentional seek priming counted as an audio underrun");
#endif
    transport.SetPlaying(false); double frozen = transport.Position; Thread.Sleep(50); Check(transport.Position == frozen, "Pause drift");
    transport.Seek(1); transport.SetDelay(500); transport.SetVolume(0); transport.SetSpeed(1.5); transport.SetPlaying(true);
    Thread.Sleep(250); Check(transport.Position is > 1.3 and < 1.5, "Seek/speed clock");
    var chart = new Chart(); chart.Mods.Add(new(.3, 0, "linear", 0, 1.5, "playspeed", -1, 0));
    chart.Mods.Add(new(.6, 0, "linear", 1, .1, "jumpto_s", -1, 1));
    transport.SetChartPlayback(new(chart, new(chart, 180, 0), true)); transport.Seek(0);
    Thread.Sleep(450); Check(transport.AudioError == null, "Route supply failed");
    Console.WriteLine("PASS audio pause, seek, delay, mute, speed, chart control, dispose");
    return;
}
#if !BASELINE
if (mode == "cpu")
{
    var chart = new Chart();
    for (int i = 0; i < 1000000; i++)
    {
        chart.Notes.Add(new(i * .01, i % 11 == 0 ? 2 : 0, i % 4, i * .01 + (i % 11 == 0 ? 100 : 0), Note.EmptyExtra));
        chart.Mods.Add(new(i * .003, 0, "linear", 0, 1, "velocity", -1, i));
    }
    var map = new BpmMap(chart, 180, 0); var watch = Stopwatch.StartNew();
    var index = new NoteIndex(chart.Notes, map); var density = new EventDensity(chart, map);
    Console.WriteLine($"million notes+events index/cache construction={watch.Elapsed.TotalMilliseconds:F3}ms");
    var old = new double[12]; var current = new double[12];
    for (int trial = 0; trial < old.Length; trial++)
    {
        watch.Restart();
        int[] bins = new int[256];
        foreach (var m in chart.Mods) { int b = (int)(map.Time(m.Beat) / 1000 * bins.Length); if (b >= 0 && b < bins.Length) bins[b]++; }
        int peak = bins.Max();
        foreach (var n in chart.Notes) { double b = map.Beat(n.Time), e = map.Beat(n.End); if (e >= 14997 && b <= 15003) GC.KeepAlive(n); }
        foreach (var n in chart.Notes) { double b = map.Beat(n.Time), e = map.Beat(n.End); if (e >= 14997 && b <= 15003) GC.KeepAlive(n); }
        old[trial] = watch.Elapsed.TotalMilliseconds;
        watch.Restart();
        _ = density.Bins(256, 1000); int visible = 0;
        for (int pass = 0; pass < 2; pass++) foreach (var e in index.Visible(4999, 5001)) { visible++; GC.KeepAlive(e.Value.Note); }
        current[trial] = watch.Elapsed.TotalMilliseconds;
        Check(peak > 0 && visible > 0, "CPU benchmark eliminated work");
    }
    Report("original full density+two note scans (same binary BPM helper)", old); Report("cached density+two interval queries", current);
    long before = GC.GetAllocatedBytesForCurrentThread();
    for (int trial = 0; trial < 1000; trial++) foreach (var e in index.Visible(4999, 5001)) _ = e.Value.Beat;
    Check(GC.GetAllocatedBytesForCurrentThread() == before, "Visible query allocates per frame");
    for (int size = 10000; size <= 1000000; size *= 10)
    {
        var same = new List<Note>(size); for (int i = 0; i < size; i++) same.Add(new(1, 0, i % 4, 1, Note.EmptyExtra));
        var ni = new NoteIndex(same, map); watch.Restart(); long found = 0;
        foreach (var e in ni.Visible(0, 1)) found++;
        Check(found == size, "Simultaneous notes were dropped"); Console.WriteLine($"same-time candidates={found} query={watch.Elapsed.TotalMilliseconds:F3}ms");
    }
    Console.WriteLine("PASS CPU scale and zero query allocations"); return;
}
#endif
using var host = new Host(hidden: true);
using var canvas = new Canvas(host.Gpu);
using var target = new Target(host.Gpu, 640, 360);
if (mode == "pixels")
{
    using var skin = new NoteSkin(canvas);
    canvas.Begin(target, 640, 360, 320, 180, new Color(.03f, .05f, .09f, 1));
    for (int skinId = 0; skinId < 4; skinId++)
        for (int type = 0; type <= 8; type++)
            for (int lane = 0; lane < (type is 1 or 7 or 8 ? 3 : 4); lane++)
            {
                float x = 24 + type * 31 + lane * 2, y = 18 + skinId * 39 + lane * 2;
                if (type == 2) skin.Hold(lane, x, y + 21, y - 8, .43, lane % 2 == 0, skinId);
                else skin.Note(type, lane, x, y, .53, (lane * 90) + (skinId % 2 == 0 ? 0 : 23), skinId);
            }
    // Mixed geometry, gradients, opacity, clip and blend boundaries exercise stream switching and uniform snapshots.
    canvas.Clip(new(0, 20, 320, 145));
    skin.Hold(0, 180, -400, 600, .65); skin.Note(1, 0, 140, 100, .5, 213);
    canvas.Blend(BlendFactor.One, BlendFactor.One); skin.Note(6, 1, 120, 120, .25, 0);
    canvas.Blend(BlendFactor.SourceAlpha, BlendFactor.InverseSourceAlpha);
    canvas.Fill(new(12, 170, 12, 2), Color.White); canvas.Clip(null);
    string output = args.Length > 3 ? args[3] : "/tmp/kg-pixels.rgba";
    File.WriteAllBytes(output, canvas.Read(target)); Console.WriteLine(output);
    return;
}
#if !BASELINE
if (mode == "readback")
{
    var slots = new[] { canvas.CreateReadback(target), canvas.CreateReadback(target), canvas.CreateReadback(target) };
    var queue = new Queue<(GpuReadback Slot, int Frame)>(); var bytes = new byte[640 * 360 * 4];
    var timer = Stopwatch.StartNew(); int rendered = 0, received = 0;
    try
    {
        while (received < 24)
        {
            Check(timer.Elapsed.TotalSeconds < 10, "Async readback stalled");
            while (Sdl.SDL_PollEvent(out _)) { }
            if (queue.TryPeek(out var frame) && frame.Slot.TryCopy(bytes))
            {
                Check(frame.Frame == received, "Readback reordered frames");
                Check(Math.Abs(bytes[0] - received * 7) <= 1 && bytes[3] == 255, "Readback was overwritten before copy");
                queue.Dequeue(); received++;
            }
            if (rendered < 24 && queue.Count < 3)
            {
                canvas.Begin(target, 640, 360, 320, 180, new Color(rendered * 7 / 255f, 0, 0, 1));
                var slot = slots[rendered % 3]; canvas.BeginReadback(slot, target); queue.Enqueue((slot, rendered++));
            }
        }
        canvas.Begin(target, 640, 360, 320, 180, Color.White);
        canvas.BeginReadback(slots[0], target); // dispose a download still in flight
    }
    finally { foreach (var slot in slots) slot.Dispose(); }
    Console.WriteLine("PASS async readback: 24 ordered frames, three-slot reuse, target overwrites, in-flight disposal"); return;
}
if (mode == "shaders")
{
    var assembly = typeof(Canvas).Assembly;
    var compiler = assembly.GetType("KuroakiGimmick.Graphics.ShaderCompiler")!;
    var vertex = (string)typeof(Canvas).GetField("NoteInstanceVertexSource", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!.GetValue(null)!;
    var fragment = (string)typeof(Canvas).GetField("NoteInstanceFragmentSource", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!.GetValue(null)!;
    var method = compiler.GetMethod("Translate", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static, null, new[] { typeof(string), typeof(bool), typeof(Sdl.GpuShaderFormat) }, null)!;
    foreach (var painterType in new[] { typeof(Canvas), typeof(TimelineNotePainter) })
    {
    if (painterType == typeof(TimelineNotePainter))
    {
        vertex = (string)painterType.GetField("Vertex", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!.GetRawConstantValue()!;
        fragment = (string)painterType.GetField("Fragment", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!.GetRawConstantValue()!;
    }
    foreach (var format in new[] { Sdl.GpuShaderFormat.SpirV, Sdl.GpuShaderFormat.Msl, Sdl.GpuShaderFormat.Dxbc })
        foreach (bool isVertex in new[] { true, false })
        {
            var translated = method.Invoke(null, new object[] { isVertex ? vertex : fragment, isVertex, format })!;
            var spirv = (byte[])translated.GetType().GetProperty("SpirV")!.GetValue(translated)!;
            var source = (string)translated.GetType().GetProperty("NativeSource")!.GetValue(translated)!;
            Check(spirv.Length > 0 && source.Length > 0, "Shader translation was empty");
            File.WriteAllText(Path.Combine(Path.GetTempPath(), $"kg-note-{format}-{isVertex}.shader"), source);
        }
    }
    Console.WriteLine("PASS note and timeline shaders: SPIR-V, MSL and HLSL translation (D3D bytecode/runtime still requires Windows)"); return;
}
#endif
#if !BASELINE
if (mode == "timeline-pixels")
{
    using var painter = new TimelineNotePainter(canvas);
    void Draw(bool instances, string output, float pixelScale)
    {
        canvas.Begin(target, 640, 360, 640 / pixelScale, 360 / pixelScale, new Color(.05f,.08f,.12f,1));
        canvas.Clip(new Rect(8.125f, 6.25f, 291.75f, 160.125f));
        foreach (float opacity in new[] { 1f, .5f, .13f })
        {
            using var fade = canvas.Opacity(opacity);
            for (int type = 0; type < 9; type++) for (int lane = 0; lane < (type is 1 or 7 or 8 ? 3 : 4); lane++)
            {
                if (type is 3 or 4 or 5) continue;
                int palette = type is 6 or 7 ? 5 : type is 1 or 8 ? 2 + lane : lane < 2 ? 0 : 1;
                var colors = new[] { (Color.Hex(0xCBDAF5),Color.Hex(0xDDE9FF)), (Color.Hex(0xF5CBDE),Color.Hex(0xFFDDED)), (Color.Hex(0x0818AD),Color.Hex(0x041EFF)), (Color.Hex(0x81009E),Color.Hex(0xD000FF)), (Color.Hex(0x980819),Color.Hex(0xF20D27)), (Color.Hex(0),Color.Hex(0xEE1C24)) };
                var r = new Rect(12+type*32+lane*5.125f, 10+(1-opacity)*110+lane*2, 7, type is 1 or 7 or 8 ? 22 : 9);
                bool selected = lane % 2 == 0;
                if (instances) painter.Draw(type,lane,r,selected);
                else
                {
                    canvas.Fill(r,colors[palette].Item1); var edge=selected?Color.White:colors[palette].Item2;
                    canvas.Fill(new(r.X,r.Y,r.W,1),edge);canvas.Fill(new(r.X,r.Y+r.H-1,r.W,1),edge);
                    canvas.Fill(new(r.X,r.Y,1,r.H),edge);canvas.Fill(new(r.X+r.W-1,r.Y,1,r.H),edge);
                }
            }
        }
        File.WriteAllBytes(output,canvas.Read(target));
    }
    foreach (float scale in new[] { 1f, 1.25f, 1.5f, 2f, 3f })
    {
    Draw(false,"/tmp/kg-timeline-legacy.rgba",scale);Draw(true,"/tmp/kg-timeline-instances.rgba",scale);
    var a=File.ReadAllBytes("/tmp/kg-timeline-legacy.rgba");var b=File.ReadAllBytes("/tmp/kg-timeline-instances.rgba");
    int max=0, changed=0;for(int i=0;i<a.Length;i++){int delta=Math.Abs(a[i]-b[i]);max=Math.Max(max,delta);if(delta>0)changed++;}
    Console.WriteLine($"timeline pixels scale={scale} differingChannels={changed} maxChannelDelta={max}");Check(max==0,"Timeline pixels changed");
    }
    return;
}
#endif
Session? session = null;
SceneRenderer? renderer = null;
NoteSkin? noteSkin = null;
KuroakiGimmick.UI.Viewer? viewer = null;
string? settingsFile = null;
if (mode is "scene" or "export" or "cancel" or "encoder-failure" or "vsm" or "uniform" or "holds" or "modulated" or "hit" or "ui")
{
    string chartPath = Path.Combine(Path.GetTempPath(), "kg-perf-" + Guid.NewGuid().ToString("N") + ".vsc");
    using (var file = new StreamWriter(chartPath))
    {
        if (mode != "vsm") for (int i = 0; i < count; i++)
        {
            double time = mode is "uniform" or "ui" ? i * 10 : mode == "holds" ? i % 1000 * 10 : 1000;
            int type = mode == "holds" ? 2 : i % 5 == 0 ? 1 : 0;
            file.WriteLine(type == 2 ? $"{time},2,{i % 4},{time + 100000}" : $"{time},{type},{i % 3}");
        }
        if (mode == "modulated") for (int i = 0; i < 10000; i++) file.WriteLine($"{i},3,0,b:{90 + i % 300}");
    }
    string? mods = null;
    if (mode is "vsm" or "ui" or "modulated" or "hit" or "ui")
    {
        var text = new System.Text.StringBuilder("!obj:obj_base_gimmick\n");
        for (int i = 0; i < (mode is "vsm" or "ui" ? count : 10000); i++) text.AppendLine($"{i * .003:R},0,linear,1,{1 + (i % 10) * .01:R},{(i % 2 == 0 ? "velocity" : "scrollspeed")},-1");
        if (mode == "modulated") text.AppendLine("0,0,linear,0,80,wave,-1\n0,0,linear,0,0.05,driven,-1\n0,0,linear,0,200,boost_time,-1\n0,0,linear,0,15,boost_distance,-1\n0,0,linear,0,15,beat,-1");
        mods = text.ToString();
    }
    try { session = new(new ViewerProject { Chart = chartPath, Profile = "core", RenderWidth = 640, GameUiEnabled = false, PostProcessing = false }, editedVsm: mods); }
    finally { File.Delete(chartPath); }
    if (mode == "ui")
    {
        settingsFile = Path.Combine(Path.GetTempPath(), "kg-ui-settings-" + Guid.NewGuid().ToString("N") + ".json");
        viewer = new(host, session, silent: true, applyPreferences: false, settingsPath: settingsFile);
    }
    else renderer = new SceneRenderer(canvas);
}
else noteSkin = new NoteSkin(canvas);
try
{
#if !BASELINE
    if (mode is "cancel" or "encoder-failure")
    {
        string output = Path.Combine(Path.GetTempPath(), "kg-cancel-" + Guid.NewGuid().ToString("N") + ".mp4");
        File.WriteAllText(output, "original"); string? previous = Environment.GetEnvironmentVariable("KUROAKI_FFMPEG"); string? fake = null;
        try
        {
            if (mode == "encoder-failure")
            {
                Check(!OperatingSystem.IsWindows(), "Fake encoder test uses a Unix script");
                fake = output + ".sh"; File.WriteAllText(fake, "#!/bin/sh\necho forced-encoder-failure >&2\nexit 23\n");
                File.SetUnixFileMode(fake, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
                Environment.SetEnvironmentVariable("KUROAKI_FFMPEG", fake);
            }
            using (var job = new VideoExport(canvas, renderer!, session!, new ExportOptions(output, 0, .5, 60, 640, 360, true, false, true)))
            {
                if (mode == "cancel") { job.Tick(); job.Tick(); job.Cancel(); job.Tick(); Check(job.Cancelled && !job.Completed, "Cancel became a success"); }
                else
                {
                    var timer = Stopwatch.StartNew(); while (job.Error == null && timer.Elapsed.TotalSeconds < 5) { job.Tick(); Thread.Sleep(1); }
                    Check(job.Error != null && !job.Completed, "Failed encoder became a success");
                }
            }
            Check(File.ReadAllText(output) == "original", "Failed export overwrote original output");
            Check(!Directory.GetFiles(Path.GetDirectoryName(output)!, "." + Path.GetFileNameWithoutExtension(output) + ".*.partial.mp4").Any(), "Partial export leaked");
            Console.WriteLine("PASS export " + mode + ": original preserved, pending frames/resources cleaned");
        }
        finally { Environment.SetEnvironmentVariable("KUROAKI_FFMPEG", previous); File.Delete(output); if (fake != null) File.Delete(fake); }
    }
    else
#endif
    if (mode == "export")
    {
        string output = args.Length > 3 ? args[3] : "/tmp/kg-perf-export.mp4";
        using var job = new VideoExport(canvas, renderer!, session!, new ExportOptions(output, 0, .5, 60, 640, 360, true, false, true));
        var timer = Stopwatch.StartNew(); double maxTick = 0;
        while (!job.Completed && job.Error == null)
        {
            while (Sdl.SDL_PollEvent(out _)) { }
            long start = Stopwatch.GetTimestamp(); job.Tick(); maxTick = Math.Max(maxTick, Stopwatch.GetElapsedTime(start).TotalMilliseconds);
            Thread.Sleep(1);
        }
        Check(job.Error == null && job.Frame == 30, job.Error ?? "Export lost frames");
        Console.WriteLine($"export frames={job.Frame} wall={timer.Elapsed.TotalSeconds:F3}s fps={job.Frame/timer.Elapsed.TotalSeconds:F2} maxTick={maxTick:F3}ms file={output}");
    }
    else
    {
        double[] record = new double[iterations], completed = new double[iterations];
        long allocated = 0, sampledPeakMemory = 0; int batchCount = 0; long uploadBytes = 0, drawCalls = 0; var process = Process.GetCurrentProcess(); TimeSpan cpuStart = process.TotalProcessorTime;
        for (int frame = -2; frame < iterations; frame++)
        {
            while (Sdl.SDL_PollEvent(out _)) { }
            if (frame == 0) allocated = GC.GetTotalAllocatedBytes(true);
#if !BASELINE
            long startBatches = PerformanceStats.Value(PerformanceStats.Counter.Batches), startUploads = PerformanceStats.Value(PerformanceStats.Counter.UploadBytes), startDraws = PerformanceStats.Value(PerformanceStats.Counter.DrawCalls);
#endif
            var timer = Stopwatch.StartNew();
            if (viewer != null) viewer.Draw(1440, 940);
            else if (renderer != null) renderer.Render(session!, mode == "uniform" ? count * .005 : mode == "holds" ? 50 : mode == "hit" ? 1.04 : .85, true, false);
            else
            {
                canvas.Begin(target, 640, 360, 320, 180, new Color(0, 0, 0, 0));
                for (int i = 0; i < count; i++)
                {
                    int type = i % 5 == 0 ? 1 : i % 13 == 0 ? 6 : 0, lane = i % 3;
                    noteSkin!.Note(type, lane, NoteSkin.LaneX(type, lane), 90, .7, 0);
                }
            }
            (viewer?.Canvas ?? canvas).Flush();
            timer.Stop();
            if (frame == 0)
            {
                var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
                var batchList = host.Gpu.GetType().GetField("batches", flags)!.GetValue(host.Gpu)!;
                var vertexList = host.Gpu.GetType().GetField("vertices", flags)!.GetValue(host.Gpu)!;
                batchCount = (int)batchList.GetType().GetProperty("Count")!.GetValue(batchList)!;
                uploadBytes = (int)vertexList.GetType().GetProperty("Count")!.GetValue(vertexList)! * 4L;
            }
            timer.Start(); host.Gpu.Submit();
            double recording = timer.Elapsed.TotalMilliseconds;
            var renderedPixels = (viewer?.Canvas ?? canvas).Read(viewer?.Renderer.Final ?? renderer?.Final ?? target); // fence-completed frame measurement, including RGBA readback
            if (frame >= 0)
            {
                record[frame] = recording; completed[frame] = timer.Elapsed.TotalMilliseconds;
#if !BASELINE
                PerformanceStats.Record(PerformanceStats.Stage.Frame, completed[frame]);
                batchCount = (int)(PerformanceStats.Value(PerformanceStats.Counter.Batches) - startBatches);
                uploadBytes = PerformanceStats.Value(PerformanceStats.Counter.UploadBytes) - startUploads;
                drawCalls = PerformanceStats.Value(PerformanceStats.Counter.DrawCalls) - startDraws;
#endif
            }
            sampledPeakMemory = Math.Max(sampledPeakMemory, Environment.WorkingSet);
            if (args.Length > 3 && frame == iterations - 1) File.WriteAllBytes(args[3], renderedPixels);
        }
        Report($"{host.Device} {mode} N={count} CPU record+submit", record); Report("fence-completed frame+readback", completed);
        Console.WriteLine($"recordedBatches/frame={batchCount} uploadBytes/frame={uploadBytes} drawCalls/frame={drawCalls}");
        process.Refresh();
        Console.WriteLine($"allocation/frame={(GC.GetTotalAllocatedBytes(true)-allocated)/(double)iterations:F0}B sampledPeakWorkingSet={sampledPeakMemory/1048576.0:F1}MiB processCpu={(process.TotalProcessorTime-cpuStart).TotalSeconds:F3}s");
    }
#if !BASELINE
    Console.WriteLine(PerformanceStats.Snapshot());
#endif
}
finally { viewer?.Dispose(); renderer?.Dispose(); noteSkin?.Dispose(); if (settingsFile != null) File.Delete(settingsFile); }
