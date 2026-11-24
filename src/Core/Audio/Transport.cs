// 协调播放时钟与 SDL 音频流，在切换曲目、定位和退出时同步管理后台补给线程。

using System.Diagnostics;
using KuroakiGimmick.Native;

namespace KuroakiGimmick.Core;

/// <summary>
/// 预览播放状态与音频设备队列。歌曲位置、用户音频延迟和音量分别管理；暂停与 seek 必须清理旧队列，避免播放过期采样。
/// </summary>
public sealed class Transport : IDisposable
{
    // THREADING: 前台控制和后台补给在同一锁内访问设备流、播放时钟与队列位置。
    readonly object audioGate = new();
    readonly AutoResetEvent feederWake = new(false);
    readonly float[] silenceBuffer = new float[8192];
    Thread? feeder;
    bool stopping, disposed, queueStarved;
    long underruns;
    /// <summary>设备播放期间从正常有数据转为空队列的累计次数，不含预填充。</summary>
    public long Underruns
    {
        get
        {
            lock (audioGate) return underruns;
        }
    }
    /// <summary>存在设备流和采样数据时进入补给逻辑的累计次数。</summary>
    public long FeedCycles
    {
        get
        {
            lock (audioGate) return feedCycles;
        }
    }
    long feedCycles;
    readonly Stopwatch watch = Stopwatch.StartNew();
    double anchor, position, speed = 1, volume = .8, duration = 1;
    AudioData? data;
    nint stream;
    int queuedSamples;
    double delaySeconds;
    ChartPlaybackMap? chartPlayback;
    NativeInterludeRoute? interludes;
    double appliedRate = 1;
    ChartPlaybackRoute? route;
    double routeOffset;
    int routeLeg;
    /// <summary>播放时钟：路线模式下的墙钟秒数，暂停时冻结在 routeOffset 上，不随 Stopwatch 继续前进。</summary>
    double RouteClock
    {
        get
        {
            lock (audioGate) return routeOffset + (Playing ? (watch.Elapsed.TotalSeconds - anchor) * speed : 0);
        }
    }
    /// <summary>记录设备初始化或后台补给失败的原因，加载新音频时清空。</summary>
    public string? AudioError { get; private set; }
    bool playing;
    /// <summary>播放时钟当前是否正在推进。</summary>
    public bool Playing
    {
        get
        {
            lock (audioGate) return playing;
        }
        private set
        {
            playing = value;
        }
    }
    /// <summary>用户选择的播放倍率，独立于谱面的 playspeed。</summary>
    public double Speed
    {
        get
        {
            lock (audioGate) return speed;
        }
    }
    /// <summary>送给 SDL 音频流的线性增益，取值 0 至 1。</summary>
    public double Volume
    {
        get
        {
            lock (audioGate) return volume;
        }
    }
    /// <summary>路线或谱面在当前位置的倍率，缺省为 1。</summary>
    public double ChartRate
    {
        get
        {
            lock (audioGate) return route?.RateAt(RouteClock) ?? chartPlayback?.RateAt(Position) ?? 1;
        }
    }
    /// <summary>用户倍速与谱面 playspeed 的乘积，即实际送给 SDL 的频率比；必须落在 0.01..100 内。</summary>
    public double EffectiveSpeed
    {
        get
        {
            lock (audioGate) return speed * ChartRate;
        }
    }
    /// <summary>当前谱面秒数。由 Stopwatch 推导而不是逐帧累加，避免掉帧造成漂移；暂停时返回冻结值。</summary>
    public double PresentationPosition
    {
        get
        {
            lock (audioGate) return Math.Clamp(!Playing ? position : route != null ? route.ChartAt(RouteClock) : chartPlayback == null ? position + (watch.Elapsed.TotalSeconds - anchor) * speed :
        chartPlayback.ChartAt(chartPlayback.PlaybackAt(position) + (watch.Elapsed.TotalSeconds - anchor) * speed), 0, duration);
        }
    }
    /// <summary>当前位置对应的歌曲秒数，插段期间按插段路线映射。</summary>
    public double Position
    {
        get
        {
            lock (audioGate) return interludes?.SongAt(PresentationPosition) ?? PresentationPosition;
        }
    }
    /// <summary>更换插段映射，同时保留当前歌曲位置并重排设备队列。</summary>
    /// <param name="value">插段路线，传 null 时恢复普通歌曲时间。</param>
    public void SetInterludes(NativeInterludeRoute? value)
    {
        lock (audioGate)
        {
            if (ReferenceEquals(interludes, value)) return;
            double song = Position, songDuration = interludes?.SongAt(duration) ?? duration;
            interludes = value;
            position = value?.PresentationAt(song) ?? song;
            duration = value?.PresentationAt(songDuration, true) ?? songDuration;
            anchor = watch.Elapsed.TotalSeconds;
            ResetRoute();
            ResetAudio();
        }
    }
    /// <summary>换用新的 playspeed 映射；先把当前位置固化再重锚时钟，否则切换瞬间会跳一段。</summary>
    /// <param name="map">要应用的谱面速度和跳转映射。</param>
    public void SetChartPlayback(ChartPlaybackMap map)
    {
        lock (audioGate)
        {
            if (ReferenceEquals(chartPlayback, map)) return;
            position = PresentationPosition;
            anchor = watch.Elapsed.TotalSeconds;
            chartPlayback = map;
            ResetRoute();
            ApplyRate();
            ResetAudio();
        }
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
        routeOffset = 0;
        routeLeg = 0;
    }
    /// <summary>打开音频设备并接管采样数据。格式固定 f32（0x8120），子系统标志 0x10 是 SDL_INIT_AUDIO。设备打开失败只记录 AudioError，预览仍可无声继续。</summary>
    /// <param name="audio">已解码的 PCM 数据，传 null 时仅初始化时钟。</param>
    /// <param name="length">呈现时长，单位为秒。</param>
    public void Load(AudioData? audio, double length)
    {
        lock (audioGate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            DisposeStream();
            Playing = false;
            position = 0;
            duration = length;
            chartPlayback = null;
            appliedRate = speed;
            interludes = null;
            route = null;
            routeOffset = 0;
            data = audio;
            AudioError = null;
            if (audio == null)
            {
                return;
            }
            if (!Sdl.SDL_InitSubSystem(0x10))
            {
                AudioError = Sdl.Error;
#if KG_SWITCH
                Console.Error.WriteLine("[K/G audio] initialization failed: " + AudioError);
#endif
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
#if KG_SWITCH
                Console.Error.WriteLine("[K/G audio] initialization failed: " + AudioError);
#endif
                return;
            }
            Sdl.Require(Sdl.SDL_SetAudioStreamFrequencyRatio(stream, (float)speed));
            Sdl.Require(Sdl.SDL_SetAudioStreamGain(stream, (float)volume));
            StartFeeder();
        }
    }

    /// <summary>切换播放状态，在结尾重新开始时复位路线。</summary>
    /// <param name="value">为 true 时播放，为 false 时冻结时钟并暂停设备。</param>
    public void SetPlaying(bool value)
    {
        lock (audioGate)
        {
            if (value == Playing)
            {
                return;
            }
            position = PresentationPosition;
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
#if KG_SWITCH
        Console.Error.WriteLine($"[K/G audio] playing={Playing} stream={stream != 0} decoded={data != null} volume={volume:F2} queuedBytes={(stream == 0 ? 0 : Sdl.SDL_GetAudioStreamQueued(stream))}");
#endif
    }

    /// <summary>编辑器把时间轴延长到音乐尾部之后，不重开音频设备。</summary>
    /// <param name="length">歌曲时间轴长度，单位为秒。</param>
    public void SetDuration(double length)
    {
        lock (audioGate)
        {
            length = Math.Max(1, interludes?.PresentationAt(length, true) ?? length);
            if (length == duration) return;
            position = PresentationPosition;
            anchor = watch.Elapsed.TotalSeconds;
            duration = length;
            ResetRoute();
        }
    }

    /// <summary>跳转到指定谱面秒数；必须重建路线并清空音频队列，否则会继续播放跳转前排好的过期采样。</summary>
    /// <param name="t">要跳转到的歌曲时刻，单位为秒。</param>
    public void Seek(double t)
    {
        lock (audioGate)
        {
            position = Math.Clamp(interludes?.PresentationAt(t) ?? t, 0, duration);
            anchor = watch.Elapsed.TotalSeconds;
            ResetRoute();
            ApplyRate();
            ResetAudio();
        }
    }

    /// <summary>按呈现秒数步进；插入剧情期间可以继续步进，而歌曲指针保持在触发处。</summary>
    /// <param name="seconds">有符号的步进量，单位为秒。</param>
    public void Step(double seconds)
    {
        lock (audioGate)
        {
            if (interludes == null)
            {
                Seek(Position + seconds);
                return;
            }
            position = Math.Clamp(PresentationPosition + seconds, 0, duration);
            anchor = watch.Elapsed.TotalSeconds;
            ResetRoute();
            ApplyRate();
            ResetAudio();
        }
    }

    /// <summary>手动倍速，夹到 0.25..2；与谱面 playspeed 相乘后才是送往设备的频率比。</summary>
    /// <param name="value">用户倍率，限制在 0.25 至 2。</param>
    public void SetSpeed(double value)
    {
        lock (audioGate)
        {
            position = PresentationPosition;
            if (route != null) routeOffset = RouteClock;
            anchor = watch.Elapsed.TotalSeconds;
            speed = Math.Clamp(value, .25, 2);
            if (stream != 0)
            {
                Sdl.SDL_SetAudioStreamFrequencyRatio(stream, (float)EffectiveSpeed);
            }
            ResetAudio();
        }
    }

    /// <summary>更新线性增益，有设备流时立即应用。</summary>
    /// <param name="value">线性增益，超出 0 至 1 的值取最近端点。</param>
    public void SetVolume(double value)
    {
        lock (audioGate)
        {
            volume = Math.Clamp(value, 0, 1);
            if (stream != 0)
            {
                Sdl.SDL_SetAudioStreamGain(stream, (float)volume);
            }
        }
    }

    /// <summary>用户音频校准延迟，输入毫秒（夹到 ±2000）内部存秒；只平移音频队列，不移动演出时钟。</summary>
    /// <param name="milliseconds">音频校准偏移，单位为毫秒。</param>
    public void SetDelay(double milliseconds)
    {
        lock (audioGate)
        {
            position = PresentationPosition;
            if (route != null) routeOffset = RouteClock;
            anchor = watch.Elapsed.TotalSeconds;
            delaySeconds = Math.Clamp(milliseconds, -2000, 2000) / 1000;
            ResetAudio();
        }
    }

    /// <summary>
    /// 时间轴秒数 → 采样帧号。delayMs 为正表示音频要晚放，因此从时间里减去。
    /// 允许返回负值（最多提前 2 秒），代表还没到音频起点，由 Feed 补静音而不是直接从 0 开始播。
    /// </summary>
    /// <param name="timelineSeconds">当前时间轴时刻，单位为秒。</param>
    /// <param name="delayMs">音频延迟，单位为毫秒。</param>
    /// <param name="rate">每秒采样帧数。</param>
    /// <param name="frames">音频的总采样帧数。</param>
    /// <returns>需排队的起始帧，负值表示应先补静音，最大值为总帧数。</returns>
    public static int AudioFrame(double timelineSeconds, double delayMs, int rate,
        int frames) => (int)Math.Clamp(Math.Floor((timelineSeconds - delayMs / 1000) * rate), -2L * rate, frames);
    /// <summary>暂停并清空设备队列后按当前位置重排；seek、变速、改延迟都必须走这里，否则会听到旧队列的残留。</summary>
    void ResetAudio()
    {
        if (stream == 0 || data == null)
        {
            return;
        }
        Sdl.SDL_PauseAudioStreamDevice(stream);
        Sdl.Require(Sdl.SDL_ClearAudioStream(stream));
        queueStarved = false;
        queuedSamples = AudioFrame(position, delaySeconds * 1000, data.Rate, data.Samples.Length / data.Channels) * data.Channels;
        Feed(priming: true);
        if (Playing)
        {
            Sdl.Require(Sdl.SDL_ResumeAudioStreamDevice(stream));
        }
    }

    /// <summary>按播放倍率补足队列，基础时长在 Switch 为 1 秒，其余平台为 0.2 秒。/4 是每个 f32 采样的字节数；queuedSamples 为负时先补静音，对应延迟造成的提前量。</summary>
    unsafe void Feed(bool priming = false)
    {
        if (stream == 0 || data == null)
        {
            return;
        }
        feedCycles++;
        int remaining = Math.Max(0, Sdl.SDL_GetAudioStreamQueued(stream)) / 4;
        // 1. 区分主动清空后的预填充与播放中发生的欠载，仅对后者计数。
        bool starved = !priming && Playing && remaining == 0 && queuedSamples > 0 && queuedSamples < data.Samples.Length;
        if (starved && !queueStarved)
        {
            underruns++;
            PerformanceStats.Add(PerformanceStats.Counter.AudioUnderruns, 1);
        }
        queueStarved = starved;
#if KG_SWITCH
        // 为解释器上可能超过 200 毫秒的帧预留更多音频。
        const double queueSeconds = 1.0;
#else
        const double queueSeconds = .2;
#endif
        // 2. 按有效倍速计算缺口，并限制在剩余采样范围内。
        int target = (int)(data.Rate * data.Channels * queueSeconds * Math.Clamp(EffectiveSpeed, .01, 100));
        int n = Math.Min(Math.Max(0, target - remaining), data.Samples.Length - queuedSamples);
        // 截断到声道数的整数倍，避免把一帧从中间劈开导致左右声道错位。
        n -= n % data.Channels;
        if (n <= 0)
        {
            return;
        }
        // 3. 先填充校准延迟所需的静音，再从对应采样开始复制。
        if (queuedSamples < 0)
        {
            int silence = Math.Min(n, -queuedSamples);
            fixed (float* zero = silenceBuffer)
            {
                int left = silence;
                while (left > 0)
                {
                    int block = Math.Min(left, silenceBuffer.Length - silenceBuffer.Length % data.Channels);
                    Sdl.Require(Sdl.SDL_PutAudioStreamData(stream, (nint)zero, block * 4));
                    left -= block;
                }
            }
            queuedSamples += silence;
            n -= silence;
            if (n == 0)
            {
                return;
            }
        }
        // SAFETY: 此处 queuedSamples 已非负，n 未超过剩余元素数，源数组在 SDL 复制期间保持固定。
        fixed (float* p = data.Samples)
        {
            Sdl.Require(Sdl.SDL_PutAudioStreamData(stream, (nint)(p + queuedSamples), n * 4));
        }
        queuedSamples += n;
    }

    /// <summary>每帧检查播放终点，并按已消费的采样量小幅修正呈现时钟。</summary>
    public void Update()
    {
        lock (audioGate)
        {
            if (AudioError != null) return;
            if (!Playing)
            {
                return;
            }
            if (PresentationPosition >= duration)
            {
                SetPlaying(false);
                return;
            }
            // 补给线程单独维护设备队列和路线，此处仅校正呈现时钟的漂移。
            if (stream != 0 && data != null)
            {
                double audioTime = (queuedSamples - Math.Max(0, Sdl.SDL_GetAudioStreamQueued(stream)) / 4) / (double)(data.Rate * data.Channels);
                // SDL 报告的排队量不含设备内部的小缓冲。用缓慢修正消除漂移，
                // 同时避免每次音频设备回调都造成一次肉眼可见的跳变。
                // 阈值 0.08 秒以内不修正；单次修正上限 ±0.003 秒，按误差的 5% 逼近。
                double error = audioTime + delaySeconds - PresentationPosition;
                if (Math.Abs(error) > .08 && audioTime < data.Duration)
                {
                    double correction = Math.Clamp(error * .05, -.003, .003);
                    if (route != null) routeOffset = Math.Clamp(RouteClock + correction / ChartRate, 0, route.Duration);
                    position = route != null ? route.ChartAt(routeOffset) : PresentationPosition + correction;
                    anchor = watch.Elapsed.TotalSeconds;
                }
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

    void StartFeeder()
    {
        if (feeder != null) return;
        feeder = new Thread(FeedLoop) { IsBackground = true, Name = "K/G audio supply" };
        feeder.Start();
    }
    // THREADING: 每隔约 10 毫秒或被唤醒后持锁检查播放状态；补给异常暂停播放，停止标记结束线程。
    void FeedLoop()
    {
        while (true)
        {
            feederWake.WaitOne(10);
            lock (audioGate)
            {
                if (stopping) return;
                if (!Playing || stream == 0 || data == null) continue;
                try
                {
                    ApplyRate();
                    if (route != null && route.IndexAt(RouteClock) != routeLeg)
                    {
                        routeLeg = route.IndexAt(RouteClock);
                        position = PresentationPosition;
                        ResetAudio();
                    }
                    if (PresentationPosition >= duration)
                    {
                        SetPlaying(false);
                        continue;
                    }
                    Feed();
                }
                catch (Exception ex)
                {
                    position = PresentationPosition;
                    Playing = false;
                    AudioError = ex.Message;
                    if (stream != 0) Sdl.SDL_PauseAudioStreamDevice(stream);
                }
            }
        }
    }
    /// <inheritdoc/>
    /// <remarks>先通知补给线程退出，待线程结束后再销毁设备流和唤醒事件。重复调用直接返回。</remarks>
    public void Dispose()
    {
        Thread? worker;
        lock (audioGate)
        {
            if (disposed) return;
            disposed = true;
            stopping = true;
            Playing = false;
            worker = feeder;
        }
        feederWake.Set();
        worker?.Join(); // THREADING: 在锁外等待，使补给线程能够取得 audioGate 并观察到停止标记。
        lock (audioGate)
        {
            DisposeStream();
            data = null;
        }
        feederWake.Dispose();
    }
}
