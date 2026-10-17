using System.Buffers;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using KuroakiGimmick.Graphics;

namespace KuroakiGimmick.Core;

/// <summary>Dedicated render threads, independent GPU contexts, and bounded ordered frame delivery.</summary>
internal sealed class ParallelExportRenderer : IDisposable
{
    readonly Channel<byte[]>[] queues;
    readonly Task[] workers;
    readonly CancellationTokenSource stop;
    readonly int totalFrames, blockFrames, frameBytes;
    Exception? failure;
    int rendered, disposed;
    public int WorkerCount => queues.Length;
    public int Rendered => Volatile.Read(ref rendered);
    public Exception? Failure => Volatile.Read(ref failure);
    internal Task Completion { get; }

    internal ParallelExportRenderer(Func<Session> createSession, string driver, ExportOptions options,
        ChartPlaybackRoute route, bool presentationRoute, int totalFrames, CancellationToken cancellation)
    {
        this.totalFrames = totalFrames;
        frameBytes = checked(options.Width * options.Height * 4);
        int count = Math.Min(options.RenderWorkers, totalFrames);
        blockFrames = Math.Min(Math.Clamp(128 * 1024 * 1024 / frameBytes / count, 1, 8), Math.Max(1, totalFrames / count));
        stop = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        queues = Enumerable.Range(0, count).Select(_ => Channel.CreateBounded<byte[]>(new BoundedChannelOptions(blockFrames)
        { SingleReader = false, SingleWriter = true, FullMode = BoundedChannelFullMode.Wait })).ToArray();
        workers = Enumerable.Repeat(Task.CompletedTask, count).ToArray();
        try
        {
            for (int i = 0; i < count; i++)
            {
                int worker = i;
                workers[i] = Task.Factory.StartNew(() => Run(worker, createSession, driver, options, route, presentationRoute),
                    CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
            }
        }
        catch
        {
            stop.Cancel(); Task.WaitAll(workers);
            foreach (var queue in queues)
                while (queue.Reader.TryRead(out var frame)) ArrayPool<byte>.Shared.Return(frame);
            stop.Dispose(); throw;
        }
        Completion = Task.WhenAll(workers);
    }

    void Run(int worker, Func<Session> createSession, string driver, ExportOptions options, ChartPlaybackRoute route, bool presentationRoute)
    {
        var token = stop.Token;
        try
        {
            token.ThrowIfCancellationRequested();
            var session = createSession();
            token.ThrowIfCancellationRequested();
            using var gpu = new GpuDevice(driver);
            using var canvas = new Canvas(gpu);
            using var renderer = new SceneRenderer(canvas) { PresentationWidth = options.Width, ExportCancellation = token };
            using var target = new Target(gpu, options.Width, options.Height);
            using var readback = canvas.CreateReadback(target);
            try
            {
                for (int block = worker; (long)block * blockFrames < totalFrames; block += WorkerCount)
                {
                    int start = block * blockFrames, end = Math.Min(totalFrames, start + blockFrames);
                    for (int frame = start; frame < end; frame++)
                    {
                        token.ThrowIfCancellationRequested();
                        double time = route.ChartAt(frame / (double)options.Fps);
                        if (presentationRoute) renderer.RenderPresentation(session, time, options.Notes, options.Effects);
                        else renderer.Render(session, time, options.Notes, options.Effects);
                        canvas.Pass(target, renderer.Final.Texture, canvas.Basic);
                        canvas.BeginReadback(readback, target);
                        while (!readback.Ready) { token.ThrowIfCancellationRequested(); Thread.Sleep(1); }
                        byte[]? bytes = ArrayPool<byte>.Shared.Rent(frameBytes);
                        try
                        {
                            if (!readback.TryCopy(bytes)) throw new InvalidOperationException("Export readback was not ready.");
                            token.ThrowIfCancellationRequested();
                            // Synchronous wait is intentional: every GPU operation stays on this dedicated thread.
                            queues[worker].Writer.WriteAsync(bytes, token).AsTask().GetAwaiter().GetResult();
                            bytes = null;
                            Interlocked.Increment(ref rendered);
                        }
                        finally { if (bytes != null) ArrayPool<byte>.Shared.Return(bytes); }
                    }
                }
            }
            catch { gpu.AbortPendingCommands(); throw; }
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        catch (Exception ex)
        {
            Interlocked.CompareExchange(ref failure, ex, null);
            stop.Cancel();
            foreach (var queue in queues) queue.Writer.TryComplete(Failure);
        }
        finally { queues[worker].Writer.TryComplete(Failure); }
    }

    internal async IAsyncEnumerable<byte[]> ReadFrames([EnumeratorCancellation] CancellationToken token)
    {
        for (int frame = 0; frame < totalFrames; frame++)
        {
            token.ThrowIfCancellationRequested();
            if (Failure is { } error) throw new IOException("Render worker failed: " + error.Message, error);
            int worker = (frame / blockFrames) % WorkerCount;
            yield return await queues[worker].Reader.ReadAsync(token).ConfigureAwait(false);
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        stop.Cancel();
        // Workers own and destroy their GPU resources. Never dispose those resources on the UI thread.
        // SDL must remain initialized until all worker-owned devices have been destroyed.
        Completion.GetAwaiter().GetResult();
        foreach (var queue in queues)
            while (queue.Reader.TryRead(out var frame)) ArrayPool<byte>.Shared.Return(frame);
        stop.Dispose();
    }
}
