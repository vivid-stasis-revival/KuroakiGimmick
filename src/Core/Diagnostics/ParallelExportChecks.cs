using System.Buffers;
using System.Security.Cryptography;
using KuroakiGimmick.Graphics;
using KuroakiGimmick.Native;

namespace KuroakiGimmick.Core;

internal static class ParallelExportChecks
{
    internal static int Run(GpuDevice gpu)
    {
        using var canvas = new Canvas(gpu);
        const int width = 640, height = 360, count = 25;
        foreach (bool feedback in new[] { false, true })
        {
            var session = new Session(new ViewerProject
            {
                RoomPreset = "none", Profile = "core", RenderWidth = 320, GameUiEnabled = false
            }, editedVsm: "!obj:obj_custom_gimmick\n!proxies:1\n0,0,linear,1,1,pra,0\n"
                + "0,4,linear,0,1,fish,-1\n0,4,linear,0,45,prz,0\n"
                + (feedback ? "0,0,linear,0.7,0.7,hom,-1\n0.6,0,linear,1,0.1,jumpto_s,-1\n1,0,linear,1,1.25,playspeed,-1\n" : ""));
            var factory = session.CaptureRenderFactory();
            double originalScroll = session.Timeline.Scroll;
            session.Project.ScrollSpeed = 19;
            session.Timeline.SetScrollSpeed(19);
            var copy = factory();
            if (copy.Timeline.Scroll != originalScroll || copy.Project.ScrollSpeed == 19
                || copy.Chart.Mods.Count != session.Chart.Mods.Count)
                throw new InvalidOperationException("Export snapshot lost unsaved events or followed later editor changes.");
            var route = feedback ? new ChartPlaybackMap(copy.Chart, copy.Timeline.Bpm, true).Route(0, 1)
                : ChartPlaybackRoute.Identity(.15, 1);
            var options = new ExportOptions("unused.mp4", .15, 1, 30, width, height, RenderWorkers: 2);
            var expected = new byte[count][];
            using (var renderer = new SceneRenderer(canvas) { PresentationWidth = width })
            using (var target = new Target(gpu, width, height))
                for (int frame = 0; frame < count; frame++)
                {
                    renderer.Render(copy, route.ChartAt(frame / 30.0));
                    canvas.Pass(target, renderer.Final.Texture, canvas.Basic);
                    expected[frame] = SHA256.HashData(canvas.Read(target));
                }
            foreach (int workers in new[] { 2, 4 })
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
                using var pool = new ParallelExportRenderer(factory, gpu.Driver, options with { RenderWorkers = workers },
                    route, false, count, timeout.Token);
                var reader = pool.ReadFrames(timeout.Token).GetAsyncEnumerator();
                try
                {
                    int frame = 0;
                    while (Wait(reader.MoveNextAsync().AsTask()))
                    {
                        var pixels = reader.Current;
                        try
                        {
                            if (!SHA256.HashData(pixels.AsSpan(0, width * height * 4)).AsSpan().SequenceEqual(expected[frame]))
                                throw new InvalidOperationException($"Parallel frame mismatch: feedback={feedback}, workers={workers}, frame={frame}.");
                        }
                        finally { ArrayPool<byte>.Shared.Return(pixels); }
                        frame++;
                    }
                    if (frame != count) throw new InvalidOperationException("Parallel export lost frames.");
                }
                finally { reader.DisposeAsync().AsTask().GetAwaiter().GetResult(); }
            }
            // Cancel with no consumer, exercising bounded-queue backpressure and worker-owned GPU cleanup.
            using (var cancellation = new CancellationTokenSource())
            using (var pool = new ParallelExportRenderer(factory, gpu.Driver, options, route, false, 1000, cancellation.Token))
            {
                var until = DateTime.UtcNow.AddSeconds(30);
                while (pool.Rendered < 16 && pool.Failure == null && DateTime.UtcNow < until)
                { Sdl.SDL_PumpEvents(); Thread.Sleep(1); }
                cancellation.Cancel();
                Wait(pool.Completion);
                if (pool.Failure != null) throw new InvalidOperationException("Cancellation failed", pool.Failure);
            }
        }
        using (var failed = new ParallelExportRenderer(() => throw new IOException("injected worker failure"), gpu.Driver,
            new ExportOptions("unused.mp4", 0, 1, RenderWorkers: 2), ChartPlaybackRoute.Identity(0, 1), false, 10, CancellationToken.None))
        {
            Wait(failed.Completion);
            if (failed.Failure?.Message != "injected worker failure") throw new InvalidOperationException("Worker failure was lost.");
        }
        Console.WriteLine("PASS parallel export: ordered raw frames match at 2/4 workers, fish/hom, backward jumps/speed changes, unsaved snapshot isolation, backpressure cancellation, worker failure.");
        return 0;
    }

    static void Wait(Task task)
    {
        while (!task.IsCompleted) { Sdl.SDL_PumpEvents(); Thread.Sleep(1); }
        task.GetAwaiter().GetResult();
    }
    static T Wait<T>(Task<T> task) { Wait((Task)task); return task.GetAwaiter().GetResult(); }
}
