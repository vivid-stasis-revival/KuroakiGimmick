using System.Diagnostics;
using KuroakiGimmick.Core;
using KuroakiGimmick.Native;

namespace KuroakiGimmick.UI;

public sealed partial class Viewer
{
    /// <summary>Bounded real-workspace regression. Stops before unsafe memory growth and preserves source files.</summary>
    public void SmokeWorkspaceMemory(int frames = 300)
    {
        if (int.TryParse(Environment.GetEnvironmentVariable("KG_WORKSPACE_MEMORY_FRAMES"), out int requested))
            frames = Math.Clamp(requested, 1, 10000);
        transport.Seek(Math.Min(124.72, Current.Duration - .1));
        transport.SetVolume(0); // exercise the physical stream without playing test audio through the speakers
        PerformanceStats.Enabled = true;
        transport.SetPlaying(Environment.GetEnvironmentVariable("KG_SWITCH_PAUSED") != "1");
        OpenEditor(); beatStart = 476;
        for (int i = 0; i < frames; i++)
        {
            while (Sdl.SDL_PollEvent(out _)) { }
            Update();
            if (!host.Gpu.PresentationReady()) { Sdl.SDL_Delay(1); i--; continue; }
            if (i == 3 || i > 3 && i % 30 == 0)
            {
                ToggleWorkspace();
                if (Environment.GetEnvironmentVariable("KG_WORKSPACE_MEMORY_LOOP") == "1")
                    transport.Seek(Math.Min(124.72, Current.Duration - .1));
            }
            long started = Stopwatch.GetTimestamp();
            Draw(); host.Gpu.Submit(present: true);
            PerformanceStats.Record(PerformanceStats.Stage.Frame, Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            long resident = Environment.WorkingSet;
            Console.WriteLine($"workspace frame={i} editor={editorMode} ms={Stopwatch.GetElapsedTime(started).TotalMilliseconds:F2} rss={resident} managed={GC.GetTotalMemory(false)}");
            if (resident > 3L * 1024 * 1024 * 1024) throw new InvalidOperationException("Workspace regression exceeded the 3 GiB memory budget; stopped.");
        }
        Console.WriteLine($"audio supplyCycles={transport.FeedCycles} observedUnderruns={transport.Underruns}");
        Console.WriteLine(PerformanceStats.Snapshot());
    }
}
