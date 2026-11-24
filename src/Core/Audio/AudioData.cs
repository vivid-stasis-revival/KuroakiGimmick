// 将解码器输出统一为交错浮点 PCM，供播放与导出共用。

using System.Diagnostics;
using KuroakiGimmick.Native;

namespace KuroakiGimmick.Core;

/// <summary>
/// 解码后的音频采样数据。Vorbis 内置解码，其它格式通过 FFmpeg；时长由采样数推导，不依赖窗口刷新速率。
/// </summary>
/// <param name="Samples">按声道交错排列的浮点采样。</param>
/// <param name="Rate">每秒采样帧数。</param>
/// <param name="Channels">每帧的声道数。</param>
public sealed record AudioData(float[] Samples, int Rate, int Channels)
{
    /// <summary>解码后单曲的字节上限；Ogg 与 FFmpeg 两条路径都要过这道检查，防止损坏的头部声明出超长音频。</summary>
    public const long MaxDecodedBytes = 1_024_000_000;
    /// <summary>检查解码采样数的符号、容量限制和可用数组长度。</summary>
    /// <param name="count">待分配的采样元素数。</param>
    /// <returns>可用作托管数组长度的采样数。</returns>
    /// <exception cref="InvalidDataException">采样数为负或超出当前解码预算。</exception>
    /// <exception cref="OverflowException">采样数无法表示为托管数组长度。</exception>
    public static int ValidateSampleCount(long count)
    {
        if (count < 0) throw new InvalidDataException("Invalid decoded audio sample count.");
        if (ChartLoadLimits.Exceeds(count, MaxDecodedBytes / sizeof(float))) throw new InvalidDataException("Decoded audio exceeds 1024 MB.");
        return checked((int)count);
    }
    /// <summary>时长由采样数推导，单位秒；不读设备状态，也不随窗口刷新率变化。</summary>
    public double Duration => Samples.Length / (double)(Rate * Channels);
    /// <summary>根据当前引擎与缓存设置解码文件为交错 PCM。</summary>
    /// <param name="path">要解码的音频文件路径。</param>
    /// <returns>包含采样率与声道数的交错 PCM。</returns>
    public static AudioData Decode(string path) => Decode(path, OptimizationEngineSettings.AudioMode, OptimizationEngineSettings.AudioCache);
    internal static AudioData Decode(string path, string engine, bool cache)
    {
        if (engine == "optimization" && OptimizationAudioBridge.TryDecode(path, cache, out var native) && native != null)
            return native;
        Console.Error.WriteLine("[KG Audio] engine=legacy (NVorbis for OGG, FFmpeg for other formats)");
        if (Path.GetExtension(path).Equals(".ogg", StringComparison.OrdinalIgnoreCase))
        {
            using var reader = new NVorbis.VorbisReader(path);
            int count = ValidateSampleCount(checked(reader.TotalSamples * reader.Channels));
            var samples = new float[count];
            int offset = 0, n;
            while (offset < samples.Length && (n = reader.ReadSamples(samples, offset, samples.Length - offset)) > 0)
            {
                offset += n;
            }
            if (offset != samples.Length)
            {
                // 实际读到的采样数可能少于头部声明：按实读长度收缩，否则尾部补零会被算进时长。
                Array.Resize(ref samples, offset);
            }
            return new(samples, reader.SampleRate, reader.Channels);
        }
#if KG_SWITCH || KG_MOBILE
        throw new PlatformNotSupportedException("This platform currently supports Ogg/Vorbis preview audio only.");
#else
        // 可选的 FFmpeg 负责 WAV/MP3/FLAC/M4A；Ogg/Vorbis 预览不需要 FFmpeg。
        // 统一解码成 f32le、双声道、48 kHz，下游按该格式直接喂 SDL 音频流。
        var info = new ProcessStartInfo(Ffmpeg.Find())
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (var a in new[]
        {
            "-v",
            "error",
            "-i",
            path,
            "-f",
            "f32le",
            "-ac",
            "2",
            "-ar",
            "48000",
            "pipe:1"
        })
        {
            info.ArgumentList.Add(a);
        }
        using var p = Process.Start(info) ?? throw new IOException("Cannot start FFmpeg.");
        var errors = p.StandardError.ReadToEndAsync();
        using var buffer = new MemoryStream();
        var block = new byte[65536];
        int read;
        while ((read = p.StandardOutput.BaseStream.Read(block)) > 0)
        {
            if (ChartLoadLimits.Exceeds(buffer.Length + read, MaxDecodedBytes))
            {
                p.Kill();
                throw new InvalidDataException("Decoded audio exceeds 1024 MB.");
            }
            buffer.Write(block, 0, read);
        }
        p.WaitForExit();
        if (p.ExitCode != 0)
        {
            throw new InvalidDataException(errors.GetAwaiter().GetResult());
        }
        if (!buffer.TryGetBuffer(out var bytes)) throw new InvalidOperationException("Decoded audio buffer is unavailable.");
        var data = new float[ValidateSampleCount(buffer.Length / sizeof(float))];
        Buffer.BlockCopy(bytes.Array!, bytes.Offset, data, 0, data.Length * sizeof(float));
        return new(data, 48000, 2);
#endif
    }
}
