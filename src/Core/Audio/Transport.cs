using System.Diagnostics;
using KuroakiGimmick.Native;

namespace KuroakiGimmick.Core;

/// <summary>
/// 预览播放状态与音频设备队列。歌曲位置、用户音频延迟和音量分别管理；暂停与 seek 必须清理旧队列，避免播放过期采样。
/// </summary>
public sealed class Transport : IDisposable
{
    readonly Stopwatch watch = Stopwatch.StartNew();
    double anchor, position, speed = 1, volume = .8, duration = 1;
    AudioData? data;
    nint stream;
    int queuedSamples;
    double delaySeconds;
    ChartPlaybackMap? chartPlayback;
    double appliedRate = 1;
    ChartPlaybackRoute? route;
    double routeOffset;
    int routeLeg;
    /// <summary>播放时钟：路线模式下的墙钟秒数，暂停时冻结在 routeOffset 上，不随 Stopwatch 继续前进。</summary>
    double RouteClock => routeOffset + (Playing ? (watch.Elapsed.TotalSeconds - anchor) * speed : 0);
    public string? AudioError { get; private set; }
    public bool Playing { get; private set; }
    public double Speed => speed;
    public double Volume => volume;
    public double ChartRate => route?.RateAt(RouteClock) ?? chartPlayback?.RateAt(Position) ?? 1;
    /// <summary>用户倍速与谱面 playspeed 的乘积，即实际送给 SDL 的频率比；必须落在 0.01..100 内。</summary>
    public double EffectiveSpeed => speed * ChartRate;
    /// <summary>当前谱面秒数。由 Stopwatch 推导而不是逐帧累加，避免掉帧造成漂移；暂停时返回冻结值。</summary>
    public double Position => Math.Clamp(!Playing ? position : route != null ? route.ChartAt(RouteClock) : chartPlayback == null ? position + (watch.Elapsed.TotalSeconds - anchor) * speed :
        chartPlayback.ChartAt(chartPlayback.PlaybackAt(position) + (watch.Elapsed.TotalSeconds - anchor) * speed), 0, duration);
    /// <summary>换用新的 playspeed 映射；先把当前位置固化再重锚时钟，否则切换瞬间会跳一段。</summary>
    public void SetChartPlayback(ChartPlaybackMap map)
    {
        if (ReferenceEquals(chartPlayback, map)) return;
        position = Position; anchor = watch.Elapsed.TotalSeconds; chartPlayback = map;
        ResetRoute();
        ApplyRate(); ResetAudio();
    }
    void ApplyRate()
    {
        double effective = EffectiveSpeed;
        if (effective is < .01 or > 100) throw new InvalidOperationException("Combined chart/manual speed is outside SDL's 0.01..100 range.");
        if (effective == appliedRate) return;
        appliedRate = effective;
        if (stream != 0) Sdl.Require(Sdl.SDL_SetAudioStreamFrequencyRatio(stream, (float)effective));
    }
    void ResetRoute()
    {
        route = chartPlayback?.HasJumps == true ? chartPlayback.Route(position, duration) : null;
        routeOffset = 0; routeLeg = 0;
    }
    /// <summary>打开音频设备并接管采样数据。格式固定 f32（0x8120），子系统标志 0x10 是 SDL_INIT_AUDIO。设备打开失败只记录 AudioError，预览仍可无声继续。</summary>
    public void Load(AudioData? audio, double length)
    {
        DisposeStream();
        Playing = false;
        position = 0;
        duration = length;
        chartPlayback = null; appliedRate = speed;
        route = null; routeOffset = 0;
        data = audio;
        AudioError = null;
        if (audio == null)
        {
            return;
        }
        if (!Sdl.SDL_InitSubSystem(0x10))
        {
            AudioError = Sdl.Error;
            return;
        }
        var spec = new Sdl.AudioSpec
        {
            Format = 0x8120,
            Channels = audio.Channels,
            Frequency = audio.Rate
        };
        stream = Sdl.SDL_OpenAudioDeviceStream(uint.MaxValue, in spec, 0, 0);
        if (stream == 0)
        {
            AudioError = Sdl.Error;
            return;
        }
        Sdl.SDL_SetAudioStreamFrequencyRatio(stream, (float) speed);
        Sdl.SDL_SetAudioStreamGain(stream, (float) volume);
    }

    public void SetPlaying(bool value)
    {
        if (value == Playing)
        {
            return;
        }
        position = Position;
        if (route != null) routeOffset = RouteClock;
        Playing = value;
        anchor = watch.Elapsed.TotalSeconds;
        if (value)
        {
            if (position >= duration)
            {
                position = 0;
                ResetRoute();
            }
            if (route != null) position = route.ChartAt(routeOffset);
            ApplyRate();
            ResetAudio();
        }
        else if (stream != 0)
        {
            Sdl.SDL_PauseAudioStreamDevice(stream);
        }
    }

    /// <summary>编辑器把时间轴延长到音乐尾部之后，不重开音频设备。</summary>
    public void SetDuration(double length)
    {
        length = Math.Max(1, length); if (length == duration) return;
        position = Position; anchor = watch.Elapsed.TotalSeconds; duration = length; ResetRoute();
    }

    /// <summary>跳转到指定谱面秒数；必须重建路线并清空音频队列，否则会继续播放跳转前排好的过期采样。</summary>
    public void Seek(double t)
    {
        position = Math.Clamp(t, 0, duration);
        anchor = watch.Elapsed.TotalSeconds;
        ResetRoute();
        ApplyRate();
        ResetAudio();
    }

    /// <summary>手动倍速，夹到 0.25..2；与谱面 playspeed 相乘后才是送往设备的频率比。</summary>
    public void SetSpeed(double value)
    {
        position = Position;
        if (route != null) routeOffset = RouteClock;
        anchor = watch.Elapsed.TotalSeconds;
        speed = Math.Clamp(value, .25, 2);
        if (stream != 0)
        {
            Sdl.SDL_SetAudioStreamFrequencyRatio(stream, (float)EffectiveSpeed);
        }
        ResetAudio();
    }

    public void SetVolume(double value)
    {
        volume = Math.Clamp(value, 0, 1);
        if (stream != 0)
        {
            Sdl.SDL_SetAudioStreamGain(stream, (float) volume);
        }
    }

    /// <summary>用户音频校准延迟，输入毫秒（夹到 ±2000）内部存秒；只平移音频队列，不移动演出时钟。</summary>
    public void SetDelay(double milliseconds)
    {
        position = Position;
        if (route != null) routeOffset = RouteClock;
        anchor = watch.Elapsed.TotalSeconds;
        delaySeconds = Math.Clamp(milliseconds, -2000, 2000) / 1000;
        ResetAudio();
    }

    /// <summary>
    /// 时间轴秒数 → 采样帧号。delayMs 为正表示音频要晚放，因此从时间里减去。
    /// 允许返回负值（最多提前 2 秒），代表还没到音频起点，由 Feed 补静音而不是直接从 0 开始播。
    /// </summary>
    public static int AudioFrame(double timelineSeconds, double delayMs, int rate,
        int frames) => (int) Math.Clamp(Math.Floor((timelineSeconds - delayMs / 1000) * rate), -2L * rate, frames);
    /// <summary>暂停并清空设备队列后按当前位置重排；seek、变速、改延迟都必须走这里，否则会听到旧队列的残留。</summary>
    void ResetAudio()
    {
        if (stream == 0 || data == null)
        {
            return;
        }
        Sdl.SDL_PauseAudioStreamDevice(stream);
        Sdl.SDL_ClearAudioStream(stream);
        queuedSamples = AudioFrame(position, delaySeconds * 1000, data.Rate, data.Samples.Length / data.Channels) * data.Channels;
        Feed();
        if (Playing)
        {
            Sdl.SDL_ResumeAudioStreamDevice(stream);
        }
    }

    /// <summary>补足约 0.2 秒的队列。/4 是每个 f32 采样的字节数；queuedSamples 为负时先补静音，对应延迟造成的提前量。</summary>
    unsafe void Feed()
    {
        if (stream == 0 || data == null)
        {
            return;
        }
        int remaining = Sdl.SDL_GetAudioStreamQueued(stream) / 4;
        int target = (int)(data.Rate * data.Channels * .2 * Math.Clamp(EffectiveSpeed, .01, 100));
        int n = Math.Min(Math.Max(0, target - remaining), data.Samples.Length - queuedSamples);
        // 截断到声道数的整数倍，避免把一帧从中间劈开导致左右声道错位。
        n -= n % data.Channels;
        if (n <= 0)
        {
            return;
        }
        if (queuedSamples < 0)
        {
            int silence = Math.Min(n, -queuedSamples);
            var zeros = new float[silence];
            fixed (float * zero = zeros)
            {
                Sdl.Require(Sdl.SDL_PutAudioStreamData(stream, (nint) zero, silence * 4));
            }
            queuedSamples += silence;
            n -= silence;
            if (n == 0)
            {
                return;
            }
        }
        fixed (float * p = data.Samples)
        {
            Sdl.Require(Sdl.SDL_PutAudioStreamData(stream, (nint)(p + queuedSamples), n * 4));
        }
        queuedSamples += n;
    }

    /// <summary>每帧推进一次：跨到路线的新 leg 时必须重排音频队列，否则跳转后仍在播旧段落。</summary>
    public void Update()
    {
        ApplyRate();
        if (!Playing)
        {
            return;
        }
        if (route != null && route.IndexAt(RouteClock) != routeLeg)
        { routeLeg = route.IndexAt(RouteClock); position = Position; ResetAudio(); }
        if (Position >= duration)
        {
            SetPlaying(false);
            return;
        }
        Feed();
        if (stream != 0 && data != null)
        {
            double audioTime = (queuedSamples - Math.Max(0, Sdl.SDL_GetAudioStreamQueued(stream)) / 4) / (double)(data.Rate * data.Channels);
            // SDL 报告的排队量不含设备内部的小缓冲。用缓慢修正消除漂移，
            // 同时避免每次音频设备回调都造成一次肉眼可见的跳变。
            // 阈值 0.08 秒以内不修正；单次修正上限 ±0.003 秒，按误差的 5% 逼近。
            double error = audioTime + delaySeconds - Position;
            if (Math.Abs(error) > .08 && audioTime < data.Duration)
            {
                double correction = Math.Clamp(error * .05, -.003, .003);
                if (route != null) routeOffset = Math.Clamp(RouteClock + correction / ChartRate, 0, route.Duration);
                position = route != null ? route.ChartAt(routeOffset) : Position + correction;
                anchor = watch.Elapsed.TotalSeconds;
            }
        }
    }

    /// <summary>设备流的唯一释放点；Dispose 与 Load 都经由它，避免重复释放或换曲时泄漏旧流。</summary>
    void DisposeStream()
    {
        if (stream != 0)
        {
            Sdl.SDL_DestroyAudioStream(stream);
            stream = 0;
        }
    }

    public void Dispose() => DisposeStream();
}
