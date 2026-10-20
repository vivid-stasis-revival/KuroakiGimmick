using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using KuroakiGimmick.Core;
using KuroakiGimmick.Graphics;
using KuroakiGimmick.Native;
using KuroakiGimmick.UI;

if (args.Length < 2)
    throw new ArgumentException("PreviewBench chart.vsc <ui|editor|preview|full|scope|settings|layers> [output-directory]");
string mode = args[1], output = args.Length > 2 ? Path.GetFullPath(args[2]) : Path.GetTempPath();
Directory.CreateDirectory(output);
Environment.SetEnvironmentVariable("KG_RUST_ENGINE", "required");
Environment.SetEnvironmentVariable("KG_LOAD_PROFILE", "1");
var settings = typeof(Session).Assembly.GetType("KuroakiGimmick.Core.OptimizationEngineSettings")!;
settings.GetMethod("Apply", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null,
    [new ViewerSettings { UnlimitedChartSize = true, CalculationEngine = "rust", AudioEngine = "nvorbis", ParallelLoading = false }]);
var watch = Stopwatch.StartNew();
var session = new Session(new ViewerProject { Chart = Path.GetFullPath(args[0]), Profile = "core", RenderWidth = 320 });
double loadMs = watch.Elapsed.TotalMilliseconds;
using var host = new Host(hidden: true);
using var viewer = new Viewer(host, session, silent: true, applyPreferences: false,
    settingsPath: Path.Combine(output, "isolated-settings.json"));
if (mode == "settings")
{
    // Exercise real buttons with isolated preferences, never the user's settings.
    var preferences = (ViewerSettings)typeof(Viewer).GetField("preferences", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(viewer)!;
    preferences.UiAnimations = false;
    preferences.Workspace.FollowDisplayScale = false; preferences.Workspace.UiScale = 1;
    preferences.StackLimit = true;
    preferences.StackLimitLayers = ViewerSettings.DefaultStackLayers;
    viewer.OpenSettings(); viewer.Draw(1440, 940);
    void Click(int x, int y)
    {
        viewer.Handle(new() { Type = 0x401, Button = 1, X = x, Y = y });
        viewer.Draw(1440, 940);
        viewer.Handle(new() { Type = 0x402, Button = 1, X = x, Y = y });
        viewer.Draw(1440, 940);
    }
    Click(200, 267); // Video navigation
    if ((string?)typeof(Viewer).GetField("settingsSection", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(viewer) != "display")
        throw new InvalidOperationException("Video navigation did not open.");
    ushort oldMods = Sdl.SDL_GetModState();
    try
    {
        void Layers(int expected)
        {
            if (preferences.StackLimitLayers != expected
                || ViewerSettings.Load(Path.Combine(output, "isolated-settings.json")).StackLimitLayers != expected)
                throw new InvalidOperationException($"Layer step/persistence: expected {expected}, got {preferences.StackLimitLayers}.");
        }
        Sdl.SDL_SetModState(0);
        Click(638, 276); Layers(320);
        Click(542, 276); Layers(256);
        Sdl.SDL_SetModState(1); // left Shift
        Click(542, 276); Layers(240);
        Sdl.SDL_SetModState(2); // right Shift
        Click(638, 276); Layers(256);
        Sdl.SDL_SetModState(0);
        preferences.StackLimitLayers = 1008;
        Click(638, 276); Layers(1024);
        Click(638, 276); Layers(1024);
        preferences.StackLimitLayers = 32;
        Click(542, 276); Layers(16);
        Click(542, 276); Layers(16);
        Sdl.SDL_SetModState(1);
        Click(638, 276); Layers(32);
    }
    finally { Sdl.SDL_SetModState(oldMods); }
    Click(470, 276); // STACK LIMIT off
    if (preferences.StackLimit || ViewerSettings.Load(Path.Combine(output, "isolated-settings.json")).StackLimit)
        throw new InvalidOperationException("STACK LIMIT off was not applied and persisted.");
    Click(370, 855); // Reset defaults
    if (!preferences.StackLimit || !ViewerSettings.Load(Path.Combine(output, "isolated-settings.json")).StackLimit
        || preferences.StackLimitLayers != ViewerSettings.DefaultStackLayers)
        throw new InvalidOperationException("Reset defaults did not restore STACK LIMIT.");
    if (!ReferenceEquals(viewer.Current, session))
        throw new InvalidOperationException("STACK LIMIT rebuilt the session.");
    viewer.Canvas.SavePng(viewer.UiTarget, Path.Combine(output, "settings.png"));
    Console.WriteLine("PASS Video > STACK LIMIT: +/-64, left/right Shift +/-16, bounds, persistence, reset, session retained.");
    return;
}
bool ui = mode is "ui" or "editor";
if (mode == "editor") viewer.OpenEditor();
PerformanceStats.Enabled = true;
int[] limits = [16, 1024, 256, 16];
var times = mode is "scope" or "layers" ? new[] { .8, .8, .8, .8 } : mode == "full" ? new[] { 0.0, .8 }
    : new[] { 0.0, .25, .8, 1.4, 2.8, 1.4, .8, .25, 0.0 };
var measurements = new List<object>();
var checksums = new Dictionary<(double Time, int Layers), string>();
foreach (double time in times)
{
    while (Sdl.SDL_PollEvent(out _)) { }
    int layerLimit = mode == "layers" ? limits[measurements.Count] : ViewerSettings.DefaultStackLayers;
    long candidatesBefore = PerformanceStats.Value(PerformanceStats.Counter.Candidates);
    watch.Restart();
    if (mode == "layers")
    {
        using (viewer.Renderer.Preview(true, layerLimit)) viewer.Renderer.Render(session, time);
    }
    else if (ui)
    {
        viewer.SetTime(time);
        viewer.Draw(1280, 800);
    }
    else if (mode == "preview" || mode == "scope" && measurements.Count == 1)
    {
        using (viewer.Renderer.Preview()) viewer.Renderer.Render(session, time);
    }
    else if (mode == "scope" && measurements.Count == 3)
    {
        using (viewer.Renderer.Preview(false)) viewer.Renderer.Render(session, time);
    }
    else if (mode is "full" or "scope") viewer.Renderer.Render(session, time);
    else throw new ArgumentException("Expected ui, editor, preview, full or scope.");
    viewer.Canvas.Flush();
    double recordedMs = watch.Elapsed.TotalMilliseconds;
    var target = ui ? viewer.UiTarget : viewer.Renderer.Final;
    byte[] pixels = viewer.Canvas.Read(target);
    double completedMs = watch.Elapsed.TotalMilliseconds;
    string checksum = Convert.ToHexString(SHA256.HashData(pixels));
    // UI contains live FPS/motion state. Scene frames must be seek-independent.
    bool compare = !ui && !(mode == "scope" && measurements.Count == 1
        && (session.NoteIndex.HasDensePreview || session.Score.HasDensePreview));
    var frameKey = (time, mode == "layers" ? layerLimit : 0);
    if (compare && checksums.TryGetValue(frameKey, out var previous) && previous != checksum)
        throw new InvalidOperationException($"Reverse seek changed pixels at {time}.");
    if (compare) checksums[frameKey] = checksum;
    long candidates = PerformanceStats.Value(PerformanceStats.Counter.Candidates) - candidatesBefore;
    measurements.Add(new { time, layerLimit, candidates, recordedMs, completedMs, checksum });
    Console.WriteLine($"frame {time:F2}: layers={layerLimit}, candidates={candidates}, record {recordedMs:F2} ms / completed {completedMs:F2} ms");
    if (time == .8) viewer.Canvas.SavePng(target, Path.Combine(output, mode + "-hit.png"));
}
viewer.Canvas.SavePng(ui ? viewer.UiTarget : viewer.Renderer.Final, Path.Combine(output, mode + ".png"));
File.WriteAllText(Path.Combine(output, mode + ".json"), JsonSerializer.Serialize(new
{
    mode, loadMs, records = session.Chart.Notes.Count, playable = session.NoteIndex.Count,
    judgements = session.Score.NoteCount, noteReduction = session.NoteIndex.HasDensePreview,
    hitReduction = session.Score.HasDensePreview, measurements,
    profile = JsonDocument.Parse(PerformanceStats.Snapshot()).RootElement
}, new JsonSerializerOptions { WriteIndented = true }));
