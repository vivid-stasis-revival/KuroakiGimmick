// 对已启用的阶段采样与性能计数生成 JSON 快照。

using System.Diagnostics;
using System.Text.Json;

namespace KuroakiGimmick.Core;

/// <summary>按需启用有界样本统计。记录 CPU 经过时间并单列等待时间，不在逐音符路径插入计时或日志。</summary>
public static class PerformanceStats
{
    /// <summary>性能采样所计量的 CPU 阶段。</summary>
    public enum Stage
    {
        /// <summary>完整一帧所耗时间。</summary>
        Frame,
        /// <summary>界面状态更新。</summary>
        UiUpdate,
        /// <summary>界面绘制。</summary>
        UiDraw,
        /// <summary>编辑器音符显示。</summary>
        EditorNotes,
        /// <summary>场景渲染。</summary>
        Scene,
        /// <summary>候选音符查询。</summary>
        Candidates,
        /// <summary>Rust 引擎调用。</summary>
        Rust,
        /// <summary>几何数据准备。</summary>
        Geometry,
        /// <summary>记录 GPU 命令。</summary>
        GpuRecord,
        /// <summary>向 GPU 上传数据。</summary>
        GpuUpload,
        /// <summary>提交 GPU 命令。</summary>
        GpuSubmit,
        /// <summary>CPU 等待 GPU 完成。</summary>
        GpuWait,
        /// <summary>读回图像数据。</summary>
        Readback,
        /// <summary>向视频编码器写入数据。</summary>
        EncoderWrite
    }
    /// <summary>按事件或数据量累加的性能计数项。</summary>
    public enum Counter
    {
        /// <summary>时间轴音符数。</summary>
        TimelineNotes,
        /// <summary>查询所得候选音符数。</summary>
        Candidates,
        /// <summary>通过可见性筛选的音符数。</summary>
        VisibleNotes,
        /// <summary>绘制调用数。</summary>
        DrawCalls,
        /// <summary>绘制批次数。</summary>
        Batches,
        /// <summary>上传字节数。</summary>
        UploadBytes,
        /// <summary>音频播放期间的队列欠载次数。</summary>
        AudioUnderruns,
        /// <summary>完成的图像读回次数。</summary>
        Readbacks,
        /// <summary>编码帧数。</summary>
        EncodedFrames,
        /// <summary>采样期间增加的托管分配字节数。</summary>
        AllocatedBytes,
        /// <summary>各代 GC 次数变化的合计。</summary>
        GcCollections,
        /// <summary>记录的 GC 暂停总微秒数。</summary>
        GcPauseMicroseconds
    }
    /// <summary>是否记录性能样本和计数，默认由 KG_PROFILE 决定。</summary>
    public static bool Enabled { get; set; } = Environment.GetEnvironmentVariable("KG_PROFILE") == "1";
    const int Window = 4096;
    sealed class Samples
    {
        public readonly double[] Values = new double[Window]; public int Count, Cursor; public long TotalCount; public double Total, Max;
    }
    static readonly Samples[] samples = Enum.GetValues<Stage>().Select(_ => new Samples()).ToArray();
    static readonly long[] counters = new long[Enum.GetValues<Counter>().Length];
    static long allocated = GC.GetTotalAllocatedBytes(false);
    static int collections = GC.CollectionCount(0) + GC.CollectionCount(1) + GC.CollectionCount(2);
    /// <summary>在释放时记录指定阶段经过时间的计时作用域。</summary>
    public readonly struct Scope : IDisposable
    {
        readonly Stage stage;
        readonly long start;
        internal Scope(Stage stage)
        {
            this.stage = stage;
            start = Enabled ? Stopwatch.GetTimestamp() : 0;
        }
        /// <inheritdoc/>
        public void Dispose()
        {
            if (start != 0) Record(stage, Stopwatch.GetElapsedTime(start).TotalMilliseconds);
        }
    }
    /// <summary>原子读取指定计数器的累计值。</summary>
    /// <param name="counter">性能计数项。</param>
    /// <returns>当前累计值。</returns>
    public static long Value(Counter counter) => Interlocked.Read(ref counters[(int)counter]);
    /// <summary>为指定阶段建立计时作用域，禁用采样时不启动计时。</summary>
    /// <param name="stage">计时阶段。</param>
    /// <returns>释放时记录经过时间的计时作用域。</returns>
    public static Scope Measure(Stage stage) => new(stage);
    /// <summary>在采样已启用时对指定计数器进行原子累加。</summary>
    /// <param name="counter">性能计数项。</param>
    /// <param name="count">要累加的数量。</param>
    public static void Add(Counter counter, long count)
    {
        if (Enabled) Interlocked.Add(ref counters[(int)counter], count);
    }
    /// <summary>更新阶段的环形时间样本，帧阶段还采集托管分配与 GC 变化。</summary>
    /// <param name="stage">计时阶段。</param>
    /// <param name="milliseconds">该阶段经过的毫秒数。</param>
    public static void Record(Stage stage, double milliseconds)
    {
        if (!Enabled) return;
        var s = samples[(int)stage];
        // THREADING: 环形写入位置、样本数和累计耗时由同一阶段的锁保护。
        lock (s)
        {
            s.Values[s.Cursor] = milliseconds;
            s.Cursor = (s.Cursor + 1) % Window;
            s.Count = Math.Min(Window, s.Count + 1);
            s.TotalCount++;
            s.Total += milliseconds;
            s.Max = Math.Max(s.Max, milliseconds);
        }
        if (stage == Stage.Frame)
        {
            long a = GC.GetTotalAllocatedBytes(false);
            Add(Counter.AllocatedBytes, Math.Max(0, a - allocated));
            allocated = a;
            int c = GC.CollectionCount(0) + GC.CollectionCount(1) + GC.CollectionCount(2);
            if (c != collections)
            {
                Add(Counter.GcCollections, c - collections);
                collections = c;
                foreach (var pause in GC.GetGCMemoryInfo().PauseDurations) Add(Counter.GcPauseMicroseconds, (long)(pause.TotalMilliseconds * 1000));
            }
        }
    }
    /// <summary>输出 JSON 快照，分位数取最近样本，均值和最大值覆盖全部已记录样本。</summary>
    /// <returns>包含阶段统计、计数器和工作集大小的 JSON 文本。</returns>
    public static string Snapshot()
    {
        using var buffer = new MemoryStream();
        using (var json = new Utf8JsonWriter(buffer))
        {
            json.WriteStartObject();
            json.WriteNumber("sampleWindow", Window);
            json.WriteStartObject("stages");
            foreach (var stage in Enum.GetValues<Stage>())
            {
                var s = samples[(int)stage];
                lock (s)
                {
                    if (s.Count == 0) continue;
                    var sorted = s.Values.AsSpan(0, s.Count).ToArray();
                    Array.Sort(sorted);
                    double P(double q) => sorted[(int)Math.Ceiling(q * sorted.Length) - 1];
                    json.WriteStartObject(stage.ToString());
                    json.WriteNumber("count", s.TotalCount);
                    json.WriteNumber("meanMs", s.Total / s.TotalCount);
                    json.WriteNumber("p50Ms", P(.5));
                    json.WriteNumber("p95Ms", P(.95));
                    json.WriteNumber("p99Ms", P(.99));
                    json.WriteNumber("maxMs", s.Max);
                    json.WriteEndObject();
                }
            }
            json.WriteEndObject();
            json.WriteStartObject("counters");
            foreach (var c in Enum.GetValues<Counter>()) json.WriteNumber(c.ToString(), Interlocked.Read(ref counters[(int)c]));
            json.WriteEndObject();
            json.WriteNumber("workingSet", Environment.WorkingSet);
            json.WriteEndObject();
        }
        return System.Text.Encoding.UTF8.GetString(buffer.ToArray());
    }

    /// <summary>启用采样且已指定 KG_PROFILE_OUT 时将统计快照写入该路径。</summary>
    public static void WriteConfiguredReport()
    {
        if (!Enabled) return;
        string? path = Environment.GetEnvironmentVariable("KG_PROFILE_OUT");
        if (!string.IsNullOrWhiteSpace(path)) File.WriteAllText(path, Snapshot());
    }
}
