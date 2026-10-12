using System.Diagnostics;
using System.Text.Json;

namespace KuroakiGimmick.Core;

/// <summary>Opt-in bounded samples, no per-note timers or logging. Timings are CPU wall time; waits are separate, not GPU execution time.</summary>
public static class PerformanceStats
{
    public enum Stage { Frame, UiUpdate, UiDraw, EditorNotes, Scene, Candidates, Rust, Geometry, GpuRecord, GpuUpload, GpuSubmit, GpuWait, Readback, EncoderWrite }
    public enum Counter { TimelineNotes, Candidates, VisibleNotes, DrawCalls, Batches, UploadBytes, AudioUnderruns, Readbacks, EncodedFrames, AllocatedBytes, GcCollections, GcPauseMicroseconds }
    public static bool Enabled { get; set; } = Environment.GetEnvironmentVariable("KG_PROFILE") == "1";
    const int Window = 4096;
    sealed class Samples { public readonly double[] Values = new double[Window]; public int Count, Cursor; public long TotalCount; public double Total, Max; }
    static readonly Samples[] samples = Enum.GetValues<Stage>().Select(_ => new Samples()).ToArray();
    static readonly long[] counters = new long[Enum.GetValues<Counter>().Length];
    static long allocated = GC.GetTotalAllocatedBytes(false);
    static int collections = GC.CollectionCount(0) + GC.CollectionCount(1) + GC.CollectionCount(2);
    public readonly struct Scope : IDisposable
    {
        readonly Stage stage;
        readonly long start;
        internal Scope(Stage stage) { this.stage = stage; start = Enabled ? Stopwatch.GetTimestamp() : 0; }
        public void Dispose() { if (start != 0) Record(stage, Stopwatch.GetElapsedTime(start).TotalMilliseconds); }
    }
    public static long Value(Counter counter) => Interlocked.Read(ref counters[(int)counter]);
    public static Scope Measure(Stage stage) => new(stage);
    public static void Add(Counter counter, long count) { if (Enabled) Interlocked.Add(ref counters[(int)counter], count); }
    public static void Record(Stage stage, double milliseconds)
    {
        if (!Enabled) return;
        var s = samples[(int)stage];
        lock (s) { s.Values[s.Cursor] = milliseconds; s.Cursor = (s.Cursor + 1) % Window; s.Count = Math.Min(Window, s.Count + 1); s.TotalCount++; s.Total += milliseconds; s.Max = Math.Max(s.Max, milliseconds); }
        if (stage == Stage.Frame)
        {
            long a = GC.GetTotalAllocatedBytes(false); Add(Counter.AllocatedBytes, Math.Max(0, a - allocated)); allocated = a;
            int c = GC.CollectionCount(0) + GC.CollectionCount(1) + GC.CollectionCount(2);
            if (c != collections)
            {
                Add(Counter.GcCollections, c - collections); collections = c;
                foreach (var pause in GC.GetGCMemoryInfo().PauseDurations) Add(Counter.GcPauseMicroseconds, (long)(pause.TotalMilliseconds * 1000));
            }
        }
    }
    public static string Snapshot()
    {
        using var buffer = new MemoryStream();
        using (var json = new Utf8JsonWriter(buffer))
        {
            json.WriteStartObject(); json.WriteNumber("sampleWindow", Window); json.WriteStartObject("stages");
            foreach (var stage in Enum.GetValues<Stage>())
            {
                var s = samples[(int)stage];
                lock (s)
                {
                    if (s.Count == 0) continue;
                    var sorted = s.Values.AsSpan(0, s.Count).ToArray(); Array.Sort(sorted);
                    double P(double q) => sorted[(int)Math.Ceiling(q * sorted.Length) - 1];
                    json.WriteStartObject(stage.ToString()); json.WriteNumber("count", s.TotalCount);
                    json.WriteNumber("meanMs", s.Total / s.TotalCount); json.WriteNumber("p50Ms", P(.5));
                    json.WriteNumber("p95Ms", P(.95)); json.WriteNumber("p99Ms", P(.99)); json.WriteNumber("maxMs", s.Max);
                    json.WriteEndObject();
                }
            }
            json.WriteEndObject(); json.WriteStartObject("counters");
            foreach (var c in Enum.GetValues<Counter>()) json.WriteNumber(c.ToString(), Interlocked.Read(ref counters[(int)c]));
            json.WriteEndObject(); json.WriteNumber("workingSet", Environment.WorkingSet); json.WriteEndObject();
        }
        return System.Text.Encoding.UTF8.GetString(buffer.ToArray());
    }

    public static void WriteConfiguredReport()
    {
        if (!Enabled) return;
        string? path = Environment.GetEnvironmentVariable("KG_PROFILE_OUT");
        if (!string.IsNullOrWhiteSpace(path)) File.WriteAllText(path, Snapshot());
    }
}
