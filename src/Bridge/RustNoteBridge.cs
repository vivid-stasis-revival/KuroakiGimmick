using System.Runtime.InteropServices;
using KuroakiGimmick.Core;

namespace KuroakiGimmick.Bridge;

/// <summary>
/// Optional RustBridge alpha 0.1.0. One native call per visible-note batch, not per note.
/// The original C# motion implementation is the fallback and reference oracle.
/// No Rust object, pointer, or GPU resource survives a call.
/// </summary>
internal static unsafe partial class RustNoteBridge
{
    internal static bool IndexedKernelAvailable => !indexedUnavailable && Enabled;
    // Rust #[repr(C)] mirror; all double fields are in exactly the same order.
    [StructLayout(LayoutKind.Sequential)]
    internal struct MotionParams
    {
        public double Beat, Alignment, Driven, BeatsPerSecond, Wave, BeatMotion;
        public uint Custom, Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MotionInput
    {
        public double Distance, EndDistance, Scroll, XOffset, YOffset, BoostTime, BoostDistance;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MotionOutput
    {
        public double XOffset, Y, EndY;
    }

    private const uint Abi = 0x00010000;
    private const string Library = "kg_native_core";
    private static string Mode => (Environment.GetEnvironmentVariable("KG_RUST_BRIDGE") ??
        (OptimizationEngineSettings.CalculationMode == "off" ? "off" : "auto")).Trim().ToLowerInvariant();
    // 0 not probed, 1 available, -1 failed. No expensive probing after failure.
    private static int status;
    private static readonly object gate = new();

    [LibraryImport(Library, EntryPoint = "kg_core_abi_version")]
    private static partial uint GetAbi();

    [LibraryImport(Library, EntryPoint = "kg_core_motion_batch")]
    private static partial int NativeBatch(MotionParams* parameters, MotionInput* inputs, nuint count, MotionOutput* outputs);

    [LibraryImport(Library, EntryPoint = "kg_core_motion_indexed")]
    private static partial int NativeIndexed(MotionParams* parameters, NoteRuntimeData* notes, nuint noteCount,
        uint* indices, nuint count, MotionInput* lanes, nuint laneCount, double scrollTime, MotionOutput* outputs);
    static bool indexedUnavailable;
    internal static bool TryEvaluateIndexed(MotionParams parameters, NoteRuntimeData[] notes, uint[] indices,
        MotionInput[] lanes, double scrollTime, MotionOutput[] outputs, int count)
    {
        if (indexedUnavailable || !EnsureAvailable() || count == 0) return false;
        if (count < 0 || count > indices.Length || count > outputs.Length) throw new ArgumentOutOfRangeException(nameof(count));
        try
        {
            using var timing = PerformanceStats.Measure(PerformanceStats.Stage.Rust);
            fixed (NoteRuntimeData* n = notes)
            fixed (uint* i = indices)
            fixed (MotionInput* l = lanes)
            fixed (MotionOutput* o = outputs)
            {
                int rc = NativeIndexed(&parameters, n, (nuint)notes.Length, i, (nuint)count, l, (nuint)lanes.Length, scrollTime, o);
                if (rc != 0) return Disable($"Indexed evaluator returned error {rc}.", false);
            }
            if (Mode == "verify")
            {
                for (int i = 0; i < count; i++)
                {
                    var n = notes[indices[i]]; var input = lanes[n.Lane];
                    input.Distance = (n.Time - scrollTime) * 1000; input.EndDistance = (n.End - scrollTime) * 1000;
                    var expected = Reference(parameters, input); var actual = outputs[i];
                    if (!Near(expected.XOffset, actual.XOffset) || !Near(expected.Y, actual.Y) || !Near(expected.EndY, actual.EndY))
                        return Disable($"Indexed motion mismatch at candidate {i}.", false);
                }
            }
            return true;
        }
        catch (EntryPointNotFoundException) { indexedUnavailable = true; return false; } // older compatible cdylib: use legacy batch
        catch (Exception e) when (e is DllNotFoundException or BadImageFormatException) { return Disable(e.Message, false); }
    }

    internal static bool Enabled => Mode != "off" && EnsureAvailable();

    private static bool EnsureAvailable(bool force = false)
    {
        if (!force && Mode == "off") return false;
        if (status == 1) return true;
        if (status == -1)
        {
            if (force || Mode == "required") throw new InvalidOperationException("RustBridge required but native core is unavailable.");
            return false;
        }
        lock (gate)
        {
            if (status == 1) return true;
            if (status == -1)
            {
                if (force || Mode == "required") throw new InvalidOperationException("RustBridge required but native core is unavailable.");
                return false;
            }
            try
            {
                if (Marshal.SizeOf<NoteRuntimeData>() != 24 || Marshal.SizeOf<MotionParams>() != 56 || Marshal.SizeOf<MotionInput>() != 56 || Marshal.SizeOf<MotionOutput>() != 24)
                    throw new InvalidOperationException("RustBridge C ABI struct layout mismatch.");
                uint abi = GetAbi();
                if (abi != Abi) throw new InvalidOperationException($"RustBridge ABI mismatch: expected 0x{Abi:X8}, got 0x{abi:X8}.");
                status = 1;
                Console.Error.WriteLine("[RustBridge 0.1.0] native motion evaluator enabled (" + Mode + ").");
                return true;
            }
            catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException or InvalidOperationException)
            {
                status = -1;
                Console.Error.WriteLine("[RustBridge 0.1.0] " + e.Message + " Using original C# evaluator.");
                if (force || Mode == "required") throw;
                return false;
            }
        }
    }

    internal static bool TryEvaluate(MotionParams parameters, MotionInput[] inputs, MotionOutput[] outputs, int count, bool force = false)
    {
        if (!EnsureAvailable(force) || count == 0) return false;
        if (count < 0 || inputs.Length < count || outputs.Length < count)
            throw new ArgumentOutOfRangeException(nameof(count));
        try
        {
            fixed (MotionInput* inputPointer = inputs)
            fixed (MotionOutput* outputPointer = outputs)
            {
                int rc = NativeBatch(&parameters, inputPointer, (nuint)count, outputPointer);
                if (rc != 0) return Disable($"Rust evaluator returned error {rc}.", force);
            }
            if (Mode == "verify" || force)
            {
                for (int i = 0; i < count; i++)
                {
                    var expected = Reference(parameters, inputs[i]);
                    var actual = outputs[i];
                    if (!Near(expected.XOffset, actual.XOffset) || !Near(expected.Y, actual.Y) || !Near(expected.EndY, actual.EndY))
                        return Disable($"Motion mismatch at note {i}: C# ({expected.XOffset:R},{expected.Y:R},{expected.EndY:R}) vs Rust ({actual.XOffset:R},{actual.Y:R},{actual.EndY:R}).", force);
                }
            }
            return true;
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            return Disable(e.Message, force);
        }
    }

    private static bool Disable(string reason, bool force)
    {
        status = -1;
        Console.Error.WriteLine("[RustBridge 0.1.0] " + reason + " Falling back to original C# motion.");
        if (force || Mode == "required") throw new InvalidOperationException("RustBridge: " + reason);
        return false;
    }

    private static bool Near(double a, double b)
    {
        if (a == b || (double.IsNaN(a) && double.IsNaN(b))) return true;
        if (!double.IsFinite(a) || !double.IsFinite(b)) return false;
        return Math.Abs(a - b) <= 1e-8 * Math.Max(1, Math.Max(Math.Abs(a), Math.Abs(b)));
    }

    /// <summary>The exact old per-note formulas, independent of Rust for diff testing.</summary>
    private static MotionOutput Reference(MotionParams p, MotionInput n)
    {
        double Y(double distance)
        {
            double y = p.Alignment + NoteMotion.YFromScroll(distance, n.YOffset, p.Driven, p.BeatsPerSecond, n.Scroll, p.Wave);
            if (p.Custom != 0)
            {
                if (n.BoostDistance != 0)
                    y += -n.BoostDistance + (distance < n.BoostTime && n.BoostTime > 0
                        ? n.BoostDistance * Math.Pow((n.BoostTime - distance) / n.BoostTime, 3) : 0);
            }
            else
            {
                y += n.BoostTime == 0 ? -n.BoostDistance
                    : -n.BoostDistance + n.BoostDistance * Math.Pow(Math.Clamp((n.BoostTime - distance) / n.BoostTime, 0, 1), 3);
            }
            return y;
        }
        return new MotionOutput
        {
            XOffset = NoteMotion.XFromValues(p.Beat, n.Distance, n.XOffset, p.BeatMotion),
            Y = Y(n.Distance),
            EndY = Y(n.EndDistance)
        };
    }

    /// <summary>CLI oracle: no SDL initialization, no chart required, requires actual native library.</summary>
    internal static void SelfTest()
    {
        EnsureAvailable(force: true);
        var rng = new Random(100100);
        const int n = 2048;
        var input = new MotionInput[n];
        var output = new MotionOutput[n];
        for (int trial = 0; trial < 16; trial++)
        {
            var p = new MotionParams
            {
                Beat = rng.NextDouble() * 20 - 6,
                Alignment = trial % 2 == 0 ? 3 : -4,
                Driven = rng.NextDouble() * 16 - 8,
                BeatsPerSecond = rng.NextDouble() * 10 + .01,
                Wave = rng.NextDouble() * 400 - 200,
                BeatMotion = rng.NextDouble() * 200 - 100,
                Custom = (uint)(trial % 2)
            };
            for (int i = 0; i < n; i++) input[i] = new MotionInput
            {
                Distance = rng.NextDouble() * 20000 - 10000,
                EndDistance = rng.NextDouble() * 20000 - 10000,
                Scroll = i % 7 == 0 ? 0 : rng.NextDouble() * 20 - 10,
                XOffset = rng.NextDouble() * 400 - 200,
                YOffset = rng.NextDouble() * 4000 - 2000,
                BoostTime = i % 11 == 0 ? 0 : rng.NextDouble() * 4000 - 1000,
                BoostDistance = i % 13 == 0 ? 0 : rng.NextDouble() * 500 - 250,
            };
            if (!TryEvaluate(p, input, output, n, force: true)) throw new Exception("RustBridge self-test did not execute native code.");
        }
        Console.WriteLine("PASS RustBridge 0.1.0: ABI and 32,768 native-vs-managed note evaluations");
    }
}
