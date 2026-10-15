using System.Text;

namespace KuroakiGimmick.Core;

/// <summary>Mix native voice cues into the same immutable PCM used by preview and export. Original song files are never modified.</summary>
public static class NativeSequenceAudio
{
    public sealed record Clip(AudioData Audio, double Time, double Gain);

    public static AudioData? Mix(AudioData? source, NativeGimmickProfile profile, Chart chart, Timeline timeline)
    {
        if (profile.Data?.Sequence is not { } sequence) return source;
        source=ApplyMusicFades(source,sequence,chart,timeline);
        var clips = new List<Clip>();
        var decoded = new Dictionary<string, AudioData>(StringComparer.Ordinal);
        foreach (var cue in sequence.AudioCues)
        {
            var triggers = chart.Mods.Where(e => e.Name == cue.Trigger).Select(e => timeline.Bpm.Time(e.Beat)).ToList();
            if (sequence.AutomaticTriggers.TryGetValue(cue.Trigger, out double automatic)) triggers.Add(automatic);
            if (triggers.Count == 0 || cue.Gain == 0) continue;
            if (triggers.Count > 64) { profile.Fail("audio/" + cue.Path, "Voice trigger count exceeds 64."); continue; }
            if (!profile.Resources.TryGetValue("audio/" + cue.Path, out string? path)) continue;
            try
            {
                if (!decoded.TryGetValue(path, out var audio)) decoded[path] = audio = AudioData.Decode(path);
                foreach (double trigger in triggers) clips.Add(new(audio, Math.Max(cue.Time, trigger), cue.Gain));
            }
            catch (Exception error) when (ResourceFiles.IsResourceError(error) || error is System.ComponentModel.Win32Exception)
            { profile.Fail("audio/" + cue.Path, "Voice decode failed: " + error.Message); }
        }
        return clips.Count == 0 ? source : MixClips(source, clips, source?.Duration ?? Math.Max(chart.Duration, timeline.End + .5));
    }

    public static AudioData? ApplyMusicFades(AudioData? source,NativeSequenceDefinition sequence,Chart chart,Timeline timeline)
    {
        if(source==null||sequence.MusicFades.Count==0)return source;
        var fades=new List<(double Start,NativeSequenceDefinition.MusicFade Fade)>();
        foreach(var fade in sequence.MusicFades)
        {
            var starts=chart.Mods.Where(e=>e.Name==fade.Trigger).Select(e=>timeline.Bpm.Time(e.Beat)).ToList();
            if(sequence.AutomaticTriggers.TryGetValue(fade.Trigger,out double automatic))starts.Add(automatic);
            if(starts.Count>0)fades.Add((Math.Max(fade.Time,starts.Min()),fade));
        }
        if(fades.Count==0)return source;
        var output=(float[])source.Samples.Clone();
        foreach(var (start,fade) in fades.OrderBy(item=>item.Start))
        {
            int first=(int)Math.Clamp(Math.Ceiling(start*source.Rate),0,output.Length/source.Channels);
            for(int frame=first;frame<output.Length/source.Channels;frame++)
            {
                double progress=fade.Duration<=0?1:Math.Clamp((frame/(double)source.Rate-start)/fade.Duration,0,1);
                double gain=fade.From+(fade.To-fade.From)*progress;
                for(int channel=0;channel<source.Channels;channel++)output[frame*source.Channels+channel]=(float)(source.Samples[frame*source.Channels+channel]*gain);
            }
        }
        return new(output,source.Rate,source.Channels);
    }

    /// <summary>Linear sample-rate conversion, mono expansion, gain and saturating summation; all positions use song seconds.</summary>
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
                int first = (int)position; float fraction = (float)(position - first);
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

    public static void WriteFloatWave(string path, AudioData audio)
    {
        using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        using var writer = new BinaryWriter(file, Encoding.ASCII, leaveOpen: true);
        int bytes = checked(audio.Samples.Length * sizeof(float));
        writer.Write("RIFF"u8); writer.Write(checked(bytes + 36)); writer.Write("WAVEfmt "u8);
        writer.Write(16); writer.Write((ushort)3); writer.Write((ushort)audio.Channels);
        writer.Write(audio.Rate); writer.Write(checked(audio.Rate * audio.Channels * 4));
        writer.Write((ushort)(audio.Channels * 4)); writer.Write((ushort)32); writer.Write("data"u8); writer.Write(bytes);
        // .NET supported targets are little endian; Write(float) defines little-endian WAV even on other hosts.
        foreach (float sample in audio.Samples) writer.Write(sample);
    }
}
