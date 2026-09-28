using System.Diagnostics;
using KuroakiGimmick.Graphics;

namespace KuroakiGimmick.Core;

/// <summary>
/// 固定帧步长导出状态机。调用同一个 SceneRenderer，编码失败或取消时释放进程与临时资源，不产出伪成功结果。
/// </summary>
// 有界 channel 给编码器提供背压，同时不阻塞 SDL 事件泵。
public sealed class VideoExport : IDisposable
{
    readonly Canvas canvas;
    readonly SceneRenderer renderer;
    readonly Session session;
    readonly Target target;
    readonly Process process;
    readonly Task<string> stderr;
    readonly Task writer;
    readonly System.Threading.Channels.Channel<byte[]> frames;
    readonly CancellationTokenSource cancel = new();
    readonly string temporary;
    readonly ChartPlaybackRoute route;
    byte[]? pending;
    bool inputCompleted, finalized;
    public ExportOptions Options { get; }
    public int Frame { get; private set; }
    public int TotalFrames { get; }
    /// <summary>已渲染帧数占总帧数的比例（0..1）。它只反映送进管道的帧，ffmpeg 收尾混流的那段时间会停在 1 附近。</summary>
    public double Progress => Frame / (double) TotalFrames;
    public bool Completed { get; private set; }
    public bool Cancelled { get; private set; }
    public bool Finalizing => inputCompleted;
    public string? Error { get; private set; }
    public string Status => Error != null ? Error : Cancelled? "Export cancelled" : Completed? "Export complete" : inputCompleted ? "Finalizing audio and MP4..." : $"Rendering frame {Frame:N0} / {TotalFrames:N0}";
    /// <summary>构造即启动 ffmpeg 子进程并建立管道；ffmpeg 不可用时直接抛错，这是 MP4 导出的硬依赖边界。</summary>
    public VideoExport(Canvas c, SceneRenderer r, Session s, ExportOptions options)
    {
        options.Validate();
        if (s.Project.Audio != null && System.IO.Path.GetFullPath(options.Path).Equals(
            System.IO.Path.GetFullPath(s.Project.Audio), StringComparison.OrdinalIgnoreCase))
            throw new IOException("Refusing to overwrite the source audio.");
        canvas = c;
        renderer = r;
        session = s;
        Options = options;
        // 挂了音频却解码失败时拒绝导出：否则会静默产出一段无声视频，属于伪成功。
        if (s.Project.Audio != null && s.Audio == null)
        {
            throw new IOException("Cannot export: selected audio failed to decode. Fix or remove the audio path first.");
        }
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(options.Path))!);
        // 先写到同目录下的隐藏 .partial.mp4，成功后才改名到目标路径；中途失败不会留下半截可播放文件。
        temporary = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(options.Path))!,
            "." + System.IO.Path.GetFileNameWithoutExtension(options.Path) + "." + Guid.NewGuid().ToString("N") + ".partial.mp4");
        // 变速段会改变输出时长，因此帧数按 route 的实际时长算，而不是 End-Start。
        route = s.Playback.Route(options.Start, options.End);
        double outputDuration = route.Duration;
        if (!double.IsFinite(outputDuration) || outputDuration <= 0 || outputDuration > 3600) throw new InvalidOperationException("Speed-adjusted export must be at most one hour.");
        TotalFrames = Math.Max(1, (int)Math.Ceiling(outputDuration * options.Fps - 1e-9));
        var p = new ProcessStartInfo(Ffmpeg.Find())
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        void Add(params string[] args)
        {
            foreach (var a in args)
            {
                p.ArgumentList.Add(a);
            }
        }
        // 用 "R" 往返格式化并固定 InvariantCulture：ffmpeg 滤镜表达式只认小数点，区域设置绝不能渗进来。
        string N(double x) => x.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
        // -n 禁止 ffmpeg 覆盖已有文件；像素格式与帧率必须与后面推送的 RGBA 原始帧完全一致。
        Add("-hide_banner", "-loglevel", "error", "-n", "-f", "rawvideo", "-pix_fmt", "rgba", "-s", $"{options.Width}x{options.Height}", "-r",
            options.Fps.ToString(), "-i", "pipe:0");
        bool audio = s.Project.Audio != null && s.Audio != null && route.Legs.Any(l => l.Start < s.Audio.Duration);
        if (audio)
        {
            Add("-i", s.Project.Audio!);
        }
        Add("-map", "0:v:0");
        if (audio)
        {
            var spans = route.Legs.ToArray();
            if (spans.Length > 512) throw new InvalidOperationException("Export exceeds 512 audio speed segments. Choose a shorter interval.");
            // 每个变速段单独截取并重采样：超出音频末尾的段用静音填充，其余用 asetrate 变调变速后再 aresample 回原采样率，
            // 最后 apad+atrim 把长度对齐到该段应有的时长，保证拼接后音画不漂移。
            var filters = spans.Select((span, i) => span.Start >= s.Audio!.Duration
                ? $"anullsrc=r={s.Audio.Rate}:cl={(s.Audio.Channels == 1 ? "mono" : "stereo")},atrim=duration={N(span.Duration)}[a{i}]"
                : $"[1:a]atrim=start={N(span.Start)}:end={N(Math.Min(span.End, s.Audio.Duration))},asetpts=PTS-STARTPTS,aresample={s.Audio.Rate},asetrate={N(s.Audio.Rate * span.Rate)},aresample={s.Audio.Rate},apad,atrim=duration={N(span.Duration)}[a{i}]").ToList();
            filters.Add(string.Concat(Enumerable.Range(0, spans.Length).Select(i => $"[a{i}]")) + $"concat=n={spans.Length}:v=0:a=1,apad[sound]");
            Add("-filter_complex", string.Join(";", filters), "-map", "[sound]", "-c:a", "aac", "-b:a", "192k");
        }
        Add("-c:v", "libx264", "-preset", "medium", "-crf", "18", "-pix_fmt", "yuv420p", "-t", N(TotalFrames / (double) options.Fps),
            "-movflags", "+faststart", temporary);
        process = Process.Start(p) ?? throw new IOException("Could not launch FFmpeg. Install FFmpeg or set KUROAKI_FFMPEG.");
        // 必须一直异步抽干 stderr，否则 ffmpeg 写满管道缓冲区会死锁；出错时它也是唯一的错误信息来源。
        stderr = process.StandardError.ReadToEndAsync();
        target = new(c.Gpu, options.Width, options.Height);
        // 容量 2：渲染线程最多领先编码器两帧，既能重叠 GPU 与编码耗时，又不会把整段视频堆在内存里。
        frames = System.Threading.Channels.Channel.CreateBounded<byte[]>(new System.Threading.Channels.BoundedChannelOptions(2)
        {
            SingleReader = true,
            SingleWriter = true
        });
        writer = Task.Run(async() =>
        {
            try
            {
                await foreach (var bytes in frames.Reader.ReadAllAsync(cancel.Token))
                {
                    await process.StandardInput.BaseStream.WriteAsync(bytes, cancel.Token);
                }
            }
            finally
            {
                // 无论正常结束还是被取消，都要关闭 stdin：ffmpeg 靠输入流结束来收尾封装。
                process.StandardInput.Close();
            }
        });
    }

    /// <summary>推进一步，不阻塞：channel 满时原样返回，等下一次调用重试。调用方负责在同一线程反复调用直到 Completed/Cancelled/Error。</summary>
    public void Tick()
    {
        if (Completed || Cancelled || Error != null)
        {
            return;
        }
        try
        {
            if (writer.IsFaulted)
            {
                throw new IOException(writer.Exception?.GetBaseException().Message);
            }
            if (process.HasExited && process.ExitCode != 0)
            {
                throw new IOException(stderr.GetAwaiter().GetResult());
            }
            // 上一帧还没送进去就先补送，绝不重新渲染——否则 Frame 计数与实际写入的帧会错位。
            if (pending != null)
            {
                if (!frames.Writer.TryWrite(pending))
                {
                    return;
                }
                pending = null;
                Frame++;
            }
            if (Frame < TotalFrames)
            {
                renderer.Render(session, route.ChartAt(Frame / (double) Options.Fps), Options.Notes, Options.Effects);
                canvas.Pass(target, renderer.Final.Texture, canvas.Basic);
                pending = canvas.Read(target);
                if (frames.Writer.TryWrite(pending))
                {
                    pending = null;
                    Frame++;
                }
                return;
            }
            if (!inputCompleted)
            {
                frames.Writer.TryComplete();
                inputCompleted = true;
            }
            // 必须等 writer 完成且 ffmpeg 退出，才能认定封装结束；提前改名会得到未写完 moov 的文件。
            if (!writer.IsCompleted || !process.HasExited)
            {
                return;
            }
            if (process.ExitCode != 0)
            {
                throw new IOException(stderr.GetAwaiter().GetResult());
            }
            // 编码及混流全部完成后才替换目标；取消或失败时原视频保持完整。
            ExportFiles.Publish(temporary, Options.Path, Options.Overwrite);
            finalized = true;
            Completed = true;
        }
        catch (Exception ex)
        {
            Error = "Export failed: " + ex.Message;
            StopEncoder();
        }
    }

    /// <summary>取消 token、关闭 channel 再杀掉 ffmpeg，顺序不能颠倒：先放行 writer 才不会把它卡在写管道上。</summary>
    void StopEncoder()
    {
        cancel.Cancel();
        frames.Writer.TryComplete();
        try
        {
            if (!process.HasExited)
            {
                process.Kill();
            }
        }
        catch (InvalidOperationException)
        {
        }
    }

    /// <summary>已完成的导出不受影响；取消后 Program 以 130 退出，与导出失败区分开。</summary>
    public void Cancel()
    {
        if (Completed)
        {
            return;
        }
        Cancelled = true;
        StopEncoder();
    }

    /// <summary>释放顺序固定：先停编码器，再等 writer 退出，最后清理临时文件与 GPU 资源。顺序颠倒会让 writer 写向已关闭的管道。</summary>
    public void Dispose()
    {
        if (!Completed)
        {
            StopEncoder();
        }
        try
        {
            // 最多等 writer 2 秒，避免卡死的编码器把退出流程一起拖住。
            writer.Wait(TimeSpan.FromSeconds(2));
        }
        catch (AggregateException)
        {
        }
        // 未成功改名的中间文件在此清理，不给用户留下无法播放的 .partial.mp4。
        if (!finalized && File.Exists(temporary))
        {
            try
            {
                File.Delete(temporary);
            }
            catch (IOException)
            {
            }
        }
        target.Dispose();
        process.Dispose();
        cancel.Dispose();
    }
}
