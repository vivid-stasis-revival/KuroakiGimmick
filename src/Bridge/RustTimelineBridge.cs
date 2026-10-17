using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace KuroakiGimmick.Core;

/// <summary>
/// Rust Optimization Engine v0.3.0: immutable, session-scoped timeline track arrays.
/// One native call produces an entire frame's base track values. Ordinary Get() uses
/// thread-local frame data without crossing FFI. Object-specific alias/per-frame/fades
/// remain in C# so that the original semantics survive this incremental migration.
/// </summary>
internal static unsafe partial class RustTimelineBridge
{
    private const string Library = "kg_native_core";
    private const uint Abi = 0x00030000;

    // Exact IDs of timeline_runtime.rs. Do not reorder without increasing ABI.
    private static readonly string[] EaseNames = [
        "linear", "inSine", "outSine", "inOutSine", "inQuad", "outQuad", "inOutQuad",
        "inCubic", "outCubic", "inOutCubic", "inQuart", "outQuart", "inOutQuart",
        "inQuint", "outQuint", "inOutQuint", "inExpo", "outExpo", "inOutExpo",
        "inCirc", "outCirc", "inOutCirc", "inBack", "outBack", "inOutBack",
        "inElastic", "outElastic", "inOutElastic", "inBounce", "outBounce", "inOutBounce"
    ];
    private static readonly Dictionary<string, uint> EaseId = EaseNames
        .Select((name, index) => (name, index))
        .ToDictionary(entry => entry.name, entry => (uint)entry.index, StringComparer.Ordinal);

    [StructLayout(LayoutKind.Sequential)]
    private struct TrackSpec
    {
        internal uint Offset, Count;
        internal double Initial;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TrackSegment
    {
        internal double Time, Duration, From, To;
        internal uint Ease, Reserved;
    }

    internal sealed class NativeTrackHandle : SafeHandle
    {
        public NativeTrackHandle() : base(IntPtr.Zero, ownsHandle: true) { }
        internal NativeTrackHandle(nint pointer) : this() => SetHandle(pointer);
        public override bool IsInvalid => handle == IntPtr.Zero;
        protected override bool ReleaseHandle()
        {
            // SafeHandle may run on the finalizer thread. Never throw from it.
            try { NativeFree(handle); return true; }
            catch (DllNotFoundException) { return false; }
            catch (EntryPointNotFoundException) { return false; }
            catch (BadImageFormatException) { return false; }
        }
    }

    [LibraryImport(Library, EntryPoint = "kg_core_tracks_abi_version")]
    private static partial uint NativeVersion();
    [LibraryImport(Library, EntryPoint = "kg_core_tracks_create")]
    private static partial int NativeCreate(TrackSpec* tracks, nuint trackCount,
        TrackSegment* segments, nuint segmentCount, out nint handle);
    [LibraryImport(Library, EntryPoint = "kg_core_tracks_eval")]
    private static partial int NativeEvaluate(NativeTrackHandle handle, double time,
        double* outputs, nuint count);
    [LibraryImport(Library, EntryPoint = "kg_core_tracks_free")]
    private static partial void NativeFree(nint handle);

    private sealed class FrameValues(int count)
    {
        internal readonly double[] Values = new double[count];
        internal double Time;
        internal bool Ready;
    }

    /// <summary>
    /// Each native handle is managed by SafeHandle. Each thread receives its own
    /// frame buffer so background snapshots cannot overwrite the main render's data.
    /// </summary>
    internal sealed class Runtime
    {
        private readonly NativeTrackHandle handle;
        private readonly Dictionary<(string Name, int Proxy), int> indices;
        private readonly (string Name, int Proxy, List<Timeline.Segment> Segments, double Initial)[] reference;
        private readonly ThreadLocal<FrameValues> frame;
        private readonly bool verify;
        private readonly bool required;
        private int disabled;
        private int reported;
        private long profiledFrames;
        private long profileTicks;
        private static readonly bool Profile = Environment.GetEnvironmentVariable("KG_RUST_TIMELINE_PROFILE") == "1";
        internal bool Active => Volatile.Read(ref disabled) == 0;

        internal Runtime(NativeTrackHandle handle,
            Dictionary<(string Name, int Proxy), int> indices,
            (string Name, int Proxy, List<Timeline.Segment> Segments, double Initial)[] reference,
            bool verify, bool required)
        {
            this.handle = handle;
            this.indices = indices;
            this.reference = reference;
            this.verify = verify;
            this.required = required;
            frame = new ThreadLocal<FrameValues>(() => new FrameValues(reference.Length));
        }

        internal void PrepareFrame(double time)
        {
            if (Volatile.Read(ref disabled) != 0) return;
            var current = frame.Value!;
            if (current.Ready && current.Time == time) return;
            current.Ready = false;
            long started = Profile ? Stopwatch.GetTimestamp() : 0;
            try
            {
                fixed (double* outputs = current.Values)
                {
                    int result = NativeEvaluate(handle, time, outputs, (nuint)current.Values.Length);
                    if (result != 0) throw new InvalidOperationException($"Rust timeline batch returned {result}.");
                }
                if (verify)
                {
                    for (int i = 0; i < reference.Length; i++)
                    {
                        var entry = reference[i];
                        double expected = Timeline.Evaluate(entry.Segments, time, entry.Initial);
                        double actual = current.Values[i];
                        if (!Near(expected, actual))
                            throw new InvalidDataException($"Track {entry.Name}/proxy={entry.Proxy} at {time:R}: C# {expected:R} != Rust {actual:R}.");
                    }
                }
                current.Time = time;
                current.Ready = true;
                if (Profile)
                {
                    Interlocked.Add(ref profileTicks, Stopwatch.GetTimestamp() - started);
                    long frames = Interlocked.Increment(ref profiledFrames);
                    if (frames % 600 == 0)
                    {
                        double averageMs = Interlocked.Read(ref profileTicks) * 1000.0 /
                            Stopwatch.Frequency / frames;
                        Console.Error.WriteLine($"[RustTimeline] evaluated {reference.Length} tracks/frame, avg={averageMs:F3} ms ({frames} frames).");
                    }
                }
            }
            catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException
                or InvalidDataException or InvalidOperationException or BadImageFormatException)
            {
                Disable(ex);
            }
        }

        internal bool TryGet(string name, int proxy, double time, out double value)
        {
            value = 0;
            if (Volatile.Read(ref disabled) != 0 || !indices.TryGetValue((name, proxy), out var slot)) return false;
            var current = frame.Value!;
            if (!current.Ready || current.Time != time) return false;
            value = current.Values[slot];
            return true;
        }

        private void Disable(Exception ex)
        {
            Volatile.Write(ref disabled, 1);
            if (Interlocked.Exchange(ref reported, 1) == 0)
                Console.Error.WriteLine("[RustTimeline] " + ex.Message + " Falling back to original C# Get().");
            if (required) throw new InvalidOperationException("Required Rust timeline failed.", ex);
        }
    }

    private static bool Near(double a, double b)
    {
        if (a == b || (double.IsNaN(a) && double.IsNaN(b))) return true;
        if (!double.IsFinite(a) || !double.IsFinite(b)) return false;
        return Math.Abs(a - b) <= 1e-8 * Math.Max(1, Math.Max(Math.Abs(a), Math.Abs(b)));
    }

    internal static Runtime? Create(
        Dictionary<(string Name, int Proxy), List<Timeline.Segment>> tracks,
        Func<string, int, double> initial)
    {
        string mode = OptimizationEngineSettings.CalculationMode;
        if (mode == "off") return null;
        bool required = mode == "required", verify = mode == "verify";
        try
        {
            if (NativeVersion() != Abi || Marshal.SizeOf<TrackSpec>() != 16 || Marshal.SizeOf<TrackSegment>() != 40)
                throw new InvalidOperationException("RustTimeline ABI/layout mismatch.");
            // Build once, after C# has applied all special event semantics and built
            // particle tables. Both languages observe identical event ordering.
            var specs = new TrackSpec[tracks.Count];
            var segments = new List<TrackSegment>();
            var indices = new Dictionary<(string Name, int Proxy), int>(tracks.Count);
            var reference = new (string Name, int Proxy, List<Timeline.Segment> Segments, double Initial)[tracks.Count];
            int index = 0;
            foreach (var track in tracks)
            {
                // List<T> uses Int32 counts, so enforce its limit before appending.
                if (track.Value.Count > int.MaxValue - segments.Count)
                    throw new InvalidDataException("RustTimeline segment count overflow.");
                double defaultValue = initial(track.Key.Name, track.Key.Proxy);
                specs[index] = new TrackSpec { Offset = (uint)segments.Count,
                    Count = (uint)track.Value.Count, Initial = defaultValue };
                indices.Add(track.Key, index);
                reference[index] = (track.Key.Name, track.Key.Proxy, track.Value, defaultValue);
                foreach (var segment in track.Value)
                {
                    if (!EaseId.TryGetValue(segment.Ease, out uint easing))
                        throw new InvalidDataException("Unsupported RustTimeline easing: " + segment.Ease);
                    segments.Add(new TrackSegment { Time = segment.Time, Duration = segment.Duration,
                        From = segment.From, To = segment.To, Ease = easing });
                }
                index++;
            }
            TrackSegment[] flat = segments.ToArray();
            nint pointer = 0;
            fixed (TrackSpec* specsPointer = specs)
            fixed (TrackSegment* segmentPointer = flat)
            {
                int rc = NativeCreate(specsPointer, (nuint)specs.Length,
                    segmentPointer, (nuint)flat.Length, out pointer);
                if (rc != 0) throw new InvalidDataException($"RustTimeline create returned {rc}.");
            }
            if (pointer == 0) throw new InvalidDataException("RustTimeline returned null handle.");
            var handle = new NativeTrackHandle(pointer);
            Console.Error.WriteLine($"[RustTimeline] batch evaluator ready: {tracks.Count} tracks, {flat.Length} segments ({mode}).");
            return new Runtime(handle, indices, reference, verify, required);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException
            or InvalidDataException or InvalidOperationException or BadImageFormatException or OverflowException)
        {
            Console.Error.WriteLine("[RustTimeline] " + ex.Message + ". Using original C# Get().");
            if (required) throw;
            return null;
        }
    }

    internal static void SelfTest()
    {
        string? old = Environment.GetEnvironmentVariable("KG_RUST_ENGINE");
        try
        {
            Environment.SetEnvironmentVariable("KG_RUST_ENGINE", "verify");
            var charts = new[]
            {
                new Chart {ObjectName = "obj_custom_gimmick", Proxies = 8},
                new Chart {ObjectName = "obj_base_gimmick", Proxies = 8}
            };
            int order = 0;
            foreach (var chart in charts)
            {
                foreach (string ease in EaseNames)
                {
                    var name = "mod_test_" + ease;
                    chart.Mods.Add(new ModEvent(0, 4, ease, -2.0, 5.0, name, -1, order++));
                }
                chart.Mods.Add(new ModEvent(0, 0, "linear", 0, 1, "pra", 4, order++));
                chart.Mods.Add(new ModEvent(0, 0, "linear", 0, 7, "pra", 4, order++));
                chart.Mods.Add(new ModEvent(2, 4, "linear", 573613, 6, "scrollspeed", -1, order++));
                chart.Mods.Add(new ModEvent(3, 2, "inQuad", 573613, 573613, "scrollspeed", -1, order++));
                var timeline = new Timeline(chart, new ViewerProject {Bpm = 120, ScrollSpeed = 3}, 2);
                if (timeline.RustTracks == null)
                    throw new InvalidOperationException("Native RustTimeline was not initialized.");
                foreach (double time in new[] {0.0, .001, .125, .5, .75, 1.0, 1.5, 2.0, 3.0, -.1, .25})
                {
                    timeline.PrepareFrame(time); // verify compares every native track against C#
                    if (!timeline.RustTracks.Active)
                        throw new InvalidDataException("RustTimeline parity check silently fell back to C#.");
                    // Both writes happen at beat 0. Earlier seek points must
                    // still return the original proxy value, not the final 7.
                    double expectedPra = time < 0 ? 0 : 7;
                    if (!timeline.RustTracks.TryGet("pra", 4, time, out double nativePra))
                        throw new InvalidDataException($"RustTimeline proxy=4 track missing at {time:R}.");
                    if (!Near(nativePra, expectedPra) || !Near(timeline.Get("pra", time, 4), expectedPra))
                        throw new InvalidDataException($"RustTimeline proxy tie order changed at {time:R}: expected {expectedPra:R}, Rust {nativePra:R}.");
                    if (!Near(timeline.Get("pra", time, 5),0))
                        throw new InvalidDataException("RustTimeline default proxy state changed.");
                }
                foreach (double speed in new[] { 1.2, 5.0, 3.0, 2.46 })
                {
                    timeline.SetScrollSpeed(speed);
                    var rebuilt = new Timeline(chart, new ViewerProject { Bpm = 120, ScrollSpeed = speed }, 2);
                    foreach (double time in new[] { -.1, 0, 1, 1.25, 1.5, 1.75, 2.5, 4 })
                    {
                        timeline.PrepareFrame(time);
                        rebuilt.PrepareFrame(time);
                        if (!timeline.RustTracks.Active || !Near(timeline.Get("scrollspeed", time), rebuilt.Get("scrollspeed", time)))
                            throw new InvalidDataException("Live scroll speed disagrees with rebuilt timeline or invalidates native verification.");
                    }
                }
            }
            Console.WriteLine("PASS RustTimeline: 31 easings, reverse seek, tied events, proxy isolation, live scroll/inherited endpoints, managed/native parity");
        }
        finally {Environment.SetEnvironmentVariable("KG_RUST_ENGINE", old);}
    }
}
