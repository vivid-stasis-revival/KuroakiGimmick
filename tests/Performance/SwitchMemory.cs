using System.Diagnostics;
using System.Reflection;
using KuroakiGimmick.Core;
using KuroakiGimmick.Graphics;
using KuroakiGimmick.Native;
using KuroakiGimmick.UI;

static class SwitchMemory
{
    public static void Run(string path, int frames)
    {
        Console.WriteLine($"pid={Environment.ProcessId} switch-memory source={path} frames={frames}");
        object?[] inputs = [path, null];
        var project = (ViewerProject)typeof(Session).GetMethod("ReadProject", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, inputs)!;
        string? projectPath = (string?)inputs[1];
        project.Gimmick = project.Gimmick?.Replace('\\', Path.DirectorySeparatorChar);
        project.WindowMotion = project.WindowMotion?.Replace('\\', Path.DirectorySeparatorChar);
        var session = new Session(project, projectPath);
        Console.WriteLine($"loaded notes={session.Chart.Notes.Count} events={session.Chart.Mods.Count} managed={GC.GetTotalMemory(false)} rss={Environment.WorkingSet}");
        using var host = new Host(hidden: false);
        string settings = Path.Combine(Path.GetTempPath(), "kg-switch-test-" + Guid.NewGuid().ToString("N") + ".json");
        if (Environment.GetEnvironmentVariable("KG_SWITCH_USER_SETTINGS") == "1") File.Copy(ViewerSettings.SettingsPath, settings);
        using var viewer = new Viewer(host, session, silent: Environment.GetEnvironmentVariable("KG_SWITCH_AUDIO_MENU") != "1", applyPreferences: true, settingsPath: settings);
        var flags = BindingFlags.NonPublic | BindingFlags.Instance;
        var transport = (Transport)typeof(Viewer).GetField("transport", flags)!.GetValue(viewer)!;
        transport.Seek(124.72);
        transport.SetPlaying(Environment.GetEnvironmentVariable("KG_SWITCH_PAUSED") != "1");
        typeof(Viewer).GetField("beatStart", flags)!.SetValue(viewer, session.Timeline.Bpm.Beat(124.72) - 10.405);
        var toggle = typeof(Viewer).GetMethod("ToggleWorkspace", flags)!;
        var update = typeof(Viewer).GetMethod("Update", flags)!;
        viewer.OpenEditor();
        typeof(Viewer).GetField("beatStart", flags)!.SetValue(viewer, session.Timeline.Bpm.Beat(124.72) - 10.405);
        for (int i = 0; i < frames; i++)
        {
            while (Sdl.SDL_PollEvent(out _)) { }
            update.Invoke(viewer, null);
            if (!host.Gpu.PresentationReady()) { Sdl.SDL_Delay(1); i--; continue; }
            if (Environment.GetEnvironmentVariable("KG_SWITCH_EDITOR_ONLY") != "1" && (i == 3 || i > 3 && i % 30 == 0)) { toggle.Invoke(viewer, null); Console.WriteLine($"toggle frame={i}"); }
            long start = Stopwatch.GetTimestamp();
            viewer.Draw(1440, 940); host.Gpu.Submit(present: true);
            long rss = Environment.WorkingSet;
            Console.WriteLine($"frame={i} ms={Stopwatch.GetElapsedTime(start).TotalMilliseconds:F2} rss={rss} managed={GC.GetTotalMemory(false)}");
            if (rss > 3L * 1024 * 1024 * 1024) throw new Exception("Test memory budget exceeded 3 GiB; stopping immediately.");
        }
        Console.WriteLine(PerformanceStats.Snapshot());
        File.Delete(settings);
    }
}
