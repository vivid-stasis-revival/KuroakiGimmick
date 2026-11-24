// 将视频帧分配给独立 GPU 工作线程，并通过有界队列按帧序交给编码端。

using System.Buffers;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using KuroakiGimmick.Graphics;

namespace KuroakiGimmick.Core;

/// <summary>专用渲染线程各自持有 GPU 上下文，并通过有容量限制的队列按帧序交付画面。</summary>
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
        // 1. 依据帧大小与线程数分配有界队列，使慢速编码器能对渲染端施加背压。
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
            // 2. 每个工作任务独占 GPU 上下文，任务内部通过关联令牌响应取消。
            for (int i = 0; i < count; i++)
            {
                int worker = i;
                workers[i] = Task.Factory.StartNew(() => Run(worker, createSession, driver, options, route, presentationRoute),
                    CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
            }
        }
        catch
        {
            stop.Cancel();
            Task.WaitAll(workers);
            foreach (var queue in queues)
                while (queue.Reader.TryRead(out var frame)) ArrayPool<byte>.Shared.Return(frame);
            stop.Dispose();
            throw;
        }
        // 3. 所有工作线程退出后，调用方才能回收尚在队列中的像素缓冲区。
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
                        while (!readback.Ready)
                        {
                            token.ThrowIfCancellationRequested();
                            Thread.Sleep(1);
                        }
                        byte[]? bytes = ArrayPool<byte>.Shared.Rent(frameBytes);
                        try
                        {
                            if (!readback.TryCopy(bytes)) throw new InvalidOperationException("Export readback was not ready.");
                            token.ThrowIfCancellationRequested();
                            // THREADING: 在专用线程同步等待队列空间，使后续 GPU 操作继续在同一线程执行。
                            queues[worker].Writer.WriteAsync(bytes, token).AsTask().GetAwaiter().GetResult();
                            // LIFETIME: 写入成功后队列接管租借的数组；失败时由本地 finally 归还。
                            bytes = null;
                            Interlocked.Increment(ref rendered);
                        }
                        finally
                        {
                            if (bytes != null) ArrayPool<byte>.Shared.Return(bytes);
                        }
                    }
                }
            }
            catch
            {
                gpu.AbortPendingCommands();
                throw;
            }
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            // 保留首个故障，通知其余线程取消并结束队列，唤醒等待读取或写入的一方。
            Interlocked.CompareExchange(ref failure, ex, null);
            stop.Cancel();
            foreach (var queue in queues) queue.Writer.TryComplete(Failure);
        }
        finally
        {
            queues[worker].Writer.TryComplete(Failure);
        }
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
        // LIFETIME: 等各工作线程销毁自身 GPU 设备后再清理队列；此期间 SDL 必须保持初始化。
        Completion.GetAwaiter().GetResult();
        foreach (var queue in queues)
            while (queue.Reader.TryRead(out var frame)) ArrayPool<byte>.Shared.Return(frame);
        stop.Dispose();
    }
}
