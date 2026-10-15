#if KG_SWITCH
using System.Diagnostics;

namespace KuroakiGimmick.Native;

/// <summary>每五秒汇总帧各阶段耗时；每帧只累加数值，不逐帧写 SD。</summary>
internal sealed class SwitchFrameMetrics
{
    long reportStart = Stopwatch.GetTimestamp();
    long input, update, draw, submit, total;
    int frames;

    internal void Record(long begin, long inputEnd, long updateEnd, long drawEnd, long submitEnd, bool playing)
    {
        input += inputEnd - begin;
        update += updateEnd - inputEnd;
        draw += drawEnd - updateEnd;
        submit += submitEnd - drawEnd;
        total += submitEnd - begin;
        frames++;
        long elapsed = submitEnd - reportStart;
        if (elapsed < Stopwatch.Frequency * 5) return;
        double scale = 1000.0 / Stopwatch.Frequency / frames;
        Console.Error.WriteLine($"[K/G perf] fps={frames * (double)Stopwatch.Frequency / elapsed:F1} playing={playing} avg_ms input={input * scale:F1} update={update * scale:F1} draw={draw * scale:F1} submit={submit * scale:F1} frame={total * scale:F1}");
        input = update = draw = submit = total = 0;
        frames = 0;
        reportStart = submitEnd;
    }
}
#endif
