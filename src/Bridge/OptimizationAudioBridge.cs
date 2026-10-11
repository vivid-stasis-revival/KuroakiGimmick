using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace KuroakiGimmick.Core;

/// <summary>Rust-owned audio decode returning a single caller-owned interleaved PCM buffer.</summary>
internal static unsafe partial class OptimizationAudioBridge
{
    const string Library = "kg_native_core";
    [StructLayout(LayoutKind.Sequential)]
    struct NativeAudio
    {
        public float* Data;
        public nuint Length;
        public uint Rate, Channels, CacheHit;
    }
    [LibraryImport(Library, EntryPoint = "kg_audio_decode")]
    private static partial int NativeDecode(byte* path, nuint pathLength, byte* cache, nuint cacheLength, NativeAudio* output);
    [LibraryImport(Library, EntryPoint = "kg_audio_free")]
    private static partial void NativeFree(float* samples, nuint length);

    internal static bool TryDecode(string file, bool cacheEnabled, out AudioData? audio)
    {
        audio = null;
        try
        {
            var watch = Stopwatch.StartNew();
            byte[] filename = Encoding.UTF8.GetBytes(Path.GetFullPath(file));
            byte[] cachePath = cacheEnabled
                ? Encoding.UTF8.GetBytes(Path.Combine(Paths.LogDirectory, "audio-cache")) : [];
            NativeAudio native = default;
            fixed (byte* pathPointer = filename)
            fixed (byte* cachePointer = cachePath)
            {
                int status = NativeDecode(pathPointer, (nuint)filename.Length, cachePointer,
                    (nuint)cachePath.Length, &native);
                if (status != 0) throw new InvalidDataException($"Rust audio decode failed ({status}).");
            }
            try
            {
                if (native.Data == null || native.Length == 0 || native.Length > (nuint)int.MaxValue ||
                    native.Rate is < 8000 or > 384000 || native.Channels is < 1 or > 32 ||
                    native.Length % (nuint)native.Channels != 0)
                    throw new InvalidDataException("Invalid native audio buffer metadata.");
                var result = new float[AudioData.ValidateSampleCount((long)native.Length)];
                Marshal.Copy((nint)native.Data, result, 0, result.Length);
                audio = new(result, checked((int)native.Rate), checked((int)native.Channels));
                string cache = native.CacheHit switch { 1 => "memory", 2 => "disk", _ => "miss" };
                Console.Error.WriteLine($"[KG Audio] engine=optimization cache={cache} samples={result.Length} decode+copy={watch.Elapsed.TotalMilliseconds:F2} ms");
                return true;
            }
            finally { if (native.Data != null) NativeFree(native.Data, native.Length); }
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException
            or InvalidDataException or IOException or ArgumentException or OverflowException)
        {
            if (Environment.GetEnvironmentVariable("KG_RUST_AUDIO_REQUIRED") == "1") throw;
            Console.Error.WriteLine($"[KG Audio] Optimization Engine unavailable: {e.Message}; falling back to NVorbis/FFmpeg.");
            return false;
        }
    }
    internal static void SelfTest()
    {
        string tmp = Path.Combine(Path.GetTempPath(), "kg-audio-native-" + Guid.NewGuid().ToString("N") + ".wav");
        // 16-bit little-endian PCM stereo, two frames.
        try
        {
            byte[] header = new byte[52];
            using (var memory = new MemoryStream(header))
            using (var writer = new BinaryWriter(memory))
            {
                writer.Write(Encoding.ASCII.GetBytes("RIFF")); writer.Write(44u);
                writer.Write(Encoding.ASCII.GetBytes("WAVEfmt ")); writer.Write(16u);
                writer.Write((ushort)1); writer.Write((ushort)2);
                writer.Write(48000u); writer.Write(192000u);
                writer.Write((ushort)4); writer.Write((ushort)16);
                writer.Write(Encoding.ASCII.GetBytes("data")); writer.Write(8u);
                writer.Write((short)0); writer.Write((short)16384);
                writer.Write((short)-16384); writer.Write((short)32767);
            }
            File.WriteAllBytes(tmp, header);
            if (!TryDecode(tmp, false, out var audio) || audio == null || audio.Rate != 48000
                || audio.Channels != 2 || audio.Samples.Length != 4)
                throw new InvalidOperationException("Optimization audio self-test did not execute native decoder.");
            Console.WriteLine("PASS Optimization Audio: Rust PCM decode + C ABI + managed copy");
        }
        finally { try { File.Delete(tmp); } catch (IOException) { } }
    }

}
