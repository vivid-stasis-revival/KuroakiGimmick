namespace KuroakiGimmick.Core;

/// <summary>
/// 常规自制协议的音符运动公式。distance 是距判定时刻的毫秒数；不能把音频校准延迟混入演出时钟。
/// </summary>
// Custom Gimmicks v1.12.7 的 csmNoteMods，音符与图片时间偏移共用这套公式。
public static class NoteMotion
{
    /// <summary>原版滚动倍率：scrollspeed × scrollindN × velocity。velocity 不做额外缩放。</summary>
    public static double ScrollMultiplier(double scrollSpeed, double laneScroll, double velocity) => scrollSpeed * laneScroll * velocity;

    /// <summary>Conservative future cutoff, in milliseconds. Past the boost transition, Y is linear plus bounded wave/boost terms.</summary>
    public static double FutureDistance(double alignment, double yOffset, double driven, double beatsPerSecond,
        double scroll, double wave, double boostTime, double boostDistance, bool custom)
    {
        if (scroll == 0) return double.PositiveInfinity;
        double center = alignment + 144 + yOffset * .1 * scroll - driven * beatsPerSecond * 22 * scroll;
        double radius = Math.Abs(wave * .2) + Math.Abs(boostDistance);
        double distance = (scroll > 0 ? center + 25 + radius : 205 - center + radius) / (.1 * Math.Abs(scroll));
        // Custom boost is cubic only before bt; afterwards its magnitude is bounded by |bd|.
        distance = Math.Max(distance, custom ? Math.Max(0, boostTime) : 0);
        return double.IsFinite(distance) ? distance + 1e-6 : double.PositiveInfinity;
    }

    /// <summary>横向偏移。以两拍为周期、0.3 拍相位提前的原版律动包络；distance 单位毫秒，结果在 320×180 逻辑空间。</summary>
    public static double X(Timeline t, double time, int lane, double distance)
    {
        double beat = (t.Bpm.Beat(time) + .3) % 2, f = beat % 1, amount = 0;
        if (f < .3)
        {
            amount = Math.Pow(f / .3, 2);
        }
        else if (f < .7)
        {
            amount = 1 - Math.Pow((f - .3) / .4, 2);
        }
        return t.Get("xoffset", time) + t.Get("xoffsetind" + lane, time) + t.Get("beat",
            time) / 100 * 11 * amount * (beat < 1 ? 1 : -1) * Math.Sin(distance / 60 + Math.PI / 2);
    }

    /// <summary>
    /// vivid/stasis 6.2.2.2 NoteModsY 的公共滚动项。velocity 不是额外位移，而是与 scrollspeed / scrollind
    /// 一起乘在完整滚动项上；把这部分集中在这里，避免普通谱面与 Custom 预览再次各自演化出一套速度语义。
    /// boost 的 Custom 扩展规则仍由各调用方保留。
    /// </summary>
    public static double YFromScroll(double distance, double yOffset, double driven, double beatsPerSecond, double scroll, double wave)
    {
        return 144 - (((distance - yOffset) * .1) + driven * beatsPerSecond * 22) * scroll
            - wave * .2 * Math.Sin(distance * .006578947368421052);
    }

    /// <summary>纵向位置。144 是判定线的 y，0.1 是毫秒到逻辑像素的原版系数；这些数值不是可调参数。</summary>
    public static double Y(Timeline t, double time, int lane, double distance)
    {
        double M(string n) => t.Get(n, time);
        double scroll = ScrollMultiplier(M("scrollspeed"), M("scrollind" + lane), M("velocity"));
        double y = YFromScroll(distance, M("yoffset") + M("yoffsetind" + lane), M("driven"),
            t.Bpm.BpmAtBeat(t.Bpm.Beat(time)) / 60, scroll, M("wave"));
        // 保留 Custom Gimmicks 的门控：逐轨 boost 只是在非零的全局 boost 之上叠加，全局为 0 时整项不生效。
        if (M("boost_distance") != 0)
        {
            double bt = M("boost_time") + M("boost_timeind" + lane), bd = M("boost_distance") + M("boost_distanceind" + lane);
            y += -bd + (distance < bt && bt > 0 ? bd * Math.Pow((bt - distance) / bt, 3) : 0);
        }
        return y;
    }
}

