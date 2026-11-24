// 解析剧情序列的语音与淡入淡出，将它们混入预览和导出的歌曲音轨。

using System.Text;

namespace KuroakiGimmick.Core;

/// <summary>将原版语音提示混入预览与导出共用的 PCM，保留原始歌曲文件。</summary>
public static class NativeSequenceAudio
{
    /// <summary>混入剧情序列的一段音频。</summary>
    /// <param name="Audio">解码后的 PCM 数据。</param>
    /// <param name="Time">开始混入的秒数。</param>
    /// <param name="Gain">线性混音增益。</param>
    public sealed record Clip(AudioData Audio, double Time, double Gain);

    /// <summary>将序列音效与源音频混合为歌曲播放数据。</summary>
    /// <param name="source">可选的源 PCM。</param>
    /// <param name="profile">原版对象音频配置。</param>
    /// <param name="chart">提供音符、演出事件及诊断的谱面。</param>
    /// <param name="timeline">用于采样轨道和回调的时间轴。</param>
    /// <returns>加入剧情音效后的音频或原始数据。</returns>
    public static AudioData? Mix(AudioData? source, NativeGimmickProfile profile, Chart chart, Timeline timeline)
    {
        if (profile.Data?.Sequence is not { } sequence) return source;
        source = ApplyMusicFades(source, sequence, chart, timeline);
        var clips = new List<Clip>();
        var decoded = new Dictionary<string, AudioData>(StringComparer.Ordinal);
        foreach (var cue in sequence.AudioCues)
        {
            var triggers = chart.Mods.Where(e => e.Name == cue.Trigger).Select(e => timeline.Bpm.Time(e.Beat)).ToList();
            if (sequence.AutomaticTriggers.TryGetValue(cue.Trigger, out double automatic)) triggers.Add(automatic);
            if (triggers.Count == 0 || cue.Gain == 0) continue;
            if (triggers.Count > 64)
            {
                profile.Fail("audio/" + cue.Path, "Voice trigger count exceeds 64.");
                continue;
            }
            if (!profile.Resources.TryGetValue("audio/" + cue.Path, out string? path)) continue;
            try
            {
                if (!decoded.TryGetValue(path, out var audio)) decoded[path] = audio = AudioData.Decode(path);
                foreach (double trigger in triggers) clips.Add(new(audio, Math.Max(cue.Time, trigger), cue.Gain));
            }
            catch (Exception error) when (ResourceFiles.IsResourceError(error) || error is System.ComponentModel.Win32Exception)
            {
                profile.Fail("audio/" + cue.Path, "Voice decode failed: " + error.Message);
            }
        }
        return clips.Count == 0 ? source : MixClips(source, clips, source?.Duration ?? Math.Max(chart.Duration, timeline.End + .5));
    }

    /// <summary>根据序列定义的音乐渐变调整对应区间的增益。</summary>
    /// <param name="source">可选的源 PCM。</param>
    /// <param name="sequence">定义剧情、音频和场景事件的序列。</param>
    /// <param name="chart">提供音符、演出事件及诊断的谱面。</param>
    /// <param name="timeline">用于采样轨道和回调的时间轴。</param>
    /// <returns>应用淡入淡出的 PCM，未应用时沿用源数据。</returns>
    public static AudioData? ApplyMusicFades(AudioData? source, NativeSequenceDefinition sequence, Chart chart, Timeline timeline)
    {
        if (source == null || sequence.MusicFades.Count == 0) return source;
        var fades = new List<(double Start, NativeSequenceDefinition.MusicFade Fade)>();
        foreach (var fade in sequence.MusicFades)
        {
            var starts = chart.Mods.Where(e => e.Name == fade.Trigger).Select(e => timeline.Bpm.Time(e.Beat)).ToList();
            if (sequence.AutomaticTriggers.TryGetValue(fade.Trigger, out double automatic)) starts.Add(automatic);
            if (starts.Count > 0) fades.Add((Math.Max(fade.Time, starts.Min()), fade));
        }
        if (fades.Count == 0) return source;
        var output = (float[])source.Samples.Clone();
        foreach (var (start, fade) in fades.OrderBy(item => item.Start))
        {
            int first = (int)Math.Clamp(Math.Ceiling(start * source.Rate), 0, output.Length / source.Channels);
            for (int frame = first; frame < output.Length / source.Channels; frame++)
            {
                double progress = fade.Duration <= 0 ? 1 : Math.Clamp((frame / (double)source.Rate - start) / fade.Duration, 0, 1);
                double gain = fade.From + (fade.To - fade.From) * progress;
                for (int channel = 0; channel < source.Channels; channel++) output[frame * source.Channels + channel] = (float)(source.Samples[frame * source.Channels + channel] * gain);
            }
        }
        return new(output, source.Rate, source.Channels);
    }

    /// <summary>按歌曲秒数定位，进行线性重采样、单声道扩展、增益调整及饱和混音。</summary>
    /// <param name="source">基础音轨，可为空。</param>
    /// <param name="clips">带时间和增益的音频片段。</param>
    /// <param name="minimumDuration">输出要求的最短秒数。</param>
    /// <returns>混音后具有统一采样格式的 PCM。</returns>
    /// <exception cref="InvalidDataException">输出采样格式或待混合的原版语音片段无效。</exception>
    public static AudioData MixClips(AudioData? source, IReadOnlyList<Clip> clips, double minimumDuration = 0)
    {
        int rate = source?.Rate ?? 48000, channels = source?.Channels ?? 2;
        if (rate <= 0 || channels is < 1 or > 2 || !double.IsFinite(minimumDuration) || minimumDuration < 0)
            throw new InvalidDataException("Invalid native voice output format.");
        double duration = Math.Max(minimumDuration, source?.Duration ?? 0);
        foreach (var clip in clips)
        {
            if (!double.IsFinite(clip.Time) || clip.Time < 0 || !double.IsFinite(clip.Gain) || clip.Gain is < 0 or > 4
                || clip.Audio.Rate <= 0 || clip.Audio.Channels is < 1 or > 2)
                throw new InvalidDataException("Invalid native voice clip.");
            duration = Math.Max(duration, clip.Time + clip.Audio.Duration);
        }
        int count = AudioData.ValidateSampleCount(checked((long)Math.Ceiling(duration * rate) * channels));
        var output = new float[count];
        if (source != null) Array.Copy(source.Samples, output, Math.Min(output.Length, source.Samples.Length));
        foreach (var clip in clips)
        {
            var audio = clip.Audio;
            int start = (int)Math.Round(clip.Time * rate), sourceFrames = audio.Samples.Length / audio.Channels;
            int frames = Math.Min(output.Length / channels - start, (int)Math.Ceiling(audio.Duration * rate));
            float Sample(int frame, int channel)
            {
                if (sourceFrames == 0) return 0;
                frame = Math.Clamp(frame, 0, sourceFrames - 1);
                return channels == 1 && audio.Channels == 2
                    ? (audio.Samples[frame * 2] + audio.Samples[frame * 2 + 1]) * .5f
                    : audio.Samples[frame * audio.Channels + Math.Min(channel, audio.Channels - 1)];
            }
            for (int frame = 0; frame < frames; frame++)
            {
                double position = frame * (double)audio.Rate / rate;
                int first = (int)position;
                float fraction = (float)(position - first);
                for (int channel = 0; channel < channels; channel++)
                {
                    float value = Sample(first, channel) * (1 - fraction) + Sample(first + 1, channel) * fraction;
                    output[(start + frame) * channels + channel] += (float)(value * clip.Gain);
                }
            }
        }
        for (int i = 0; i < output.Length; i++) output[i] = Math.Clamp(output[i], -1, 1);
        return new(output, rate, channels);
    }

    /// <summary>将交错浮点采样写为 WAVE 音频。</summary>
    /// <param name="path">输出 WAVE 路径。</param>
    /// <param name="audio">要写入的浮点 PCM。</param>
    public static void WriteFloatWave(string path, AudioData audio)
    {
        using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        using var writer = new BinaryWriter(file, Encoding.ASCII, leaveOpen: true);
        int bytes = checked(audio.Samples.Length * sizeof(float));
        writer.Write("RIFF"u8);
        writer.Write(checked(bytes + 36));
        writer.Write("WAVEfmt "u8);
        writer.Write(16);
        writer.Write((ushort)3);
        writer.Write((ushort)audio.Channels);
        writer.Write(audio.Rate);
        writer.Write(checked(audio.Rate * audio.Channels * 4));
        writer.Write((ushort)(audio.Channels * 4));
        writer.Write((ushort)32);
        writer.Write("data"u8);
        writer.Write(bytes);
        // 支持的 .NET 目标使用小端；BinaryWriter.Write(float) 在其他主机上同样按 WAVE 的小端格式写出。
        foreach (float sample in audio.Samples) writer.Write(sample);
    }
}
