namespace KuroakiGimmick.Core;

/// <summary>
/// 常规自制协议的音符运动公式。distance 是距判定时刻的毫秒数；不能把音频校准延迟混入演出时钟。
/// </summary>
// Custom Gimmicks v1.12.7 的 csmNoteMods，音符与图片时间偏移共用这套公式。
public static class NoteMotion
{
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

    /// <summary>纵向位置。144 是判定线的 y，0.1 是毫秒到逻辑像素的原版系数；这些数值不是可调参数。</summary>
    public static double Y(Timeline t, double time, int lane, double distance)
    {
        double M(string n) => t.Get(n, time);
        double scroll = M("scrollspeed") * M("scrollind" + lane) * M("velocity");
        double y = 144 - (distance - M("yoffset") - M("yoffsetind" + lane)) * .1 * scroll - M("driven") * t.Bpm.BpmAtBeat(t.Bpm.Beat(time)) / 60 * scroll * 22 - M("wave") * .2 * Math.Sin(distance / 152);
        // 保留源码的门控：逐轨道 boost 只是在非零的全局 boost 之上叠加，全局为 0 时整项不生效。
        if (M("boost_distance") != 0)
        {
            double bt = M("boost_time") + M("boost_timeind" + lane), bd = M("boost_distance") + M("boost_distanceind" + lane);
            y += - bd + (distance < bt && bt > 0 ? bd * Math.Pow((bt - distance) / bt, 3) : 0);
        }
        return y;
    }
}

