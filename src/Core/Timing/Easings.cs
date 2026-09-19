namespace KuroakiGimmick.Core;

/// <summary>
/// 缓动函数及其规范化名称。输入在统一边界夹取，端点精确返回 0/1，避免插值完成后漂移。
/// </summary>
public static class Easings
{
    static readonly HashSet<string> Known = new("linear inSine outSine inOutSine inQuad outQuad inOutQuad inCubic outCubic inOutCubic inQuart outQuart inOutQuart inQuint outQuint inOutQuint inExpo outExpo inOutExpo inCirc outCirc inOutCirc inBack outBack inOutBack inElastic outElastic inOutElastic inBounce outBounce inOutBounce".Split(' '));
    /// <summary>去掉 “Ease” 前缀并做大小写无关匹配；无法识别时原样返回，让诊断能报出作者写的原拼写。</summary>
    public static string Normalize(string s)
    {
        s = s.Trim();
        if (s.StartsWith("Ease", StringComparison.OrdinalIgnoreCase))
        {
            s = s[4..];
        }
        return Known.FirstOrDefault(k => k.Equals(s, StringComparison.OrdinalIgnoreCase)) ?? s;
    }

    public static bool IsKnown(string s) => Known.Contains(Normalize(s));
    /// <summary>求缓动值；t 先夹到 [0,1]，端点精确返回 0/1。名称未知时抛异常，由调用方转成诊断，不静默退成 linear。</summary>
    public static double Eval(string ease, double t)
    {
        t = Math.Clamp(t, 0, 1);
        // 1.70158 是 back 系列标准的过冲常数，inOutBack 再乘 1.525；数值取自原版，改动会改变过冲幅度。
        const double back = 1.70158;
        if (t is 0 or 1)
        {
            return t;
        }
        return ease switch
        {
            "linear" => t,
            "inSine" => 1 - Math.Cos(t * Math.PI / 2),
            "outSine" => Math.Sin(t * Math.PI / 2),
            "inOutSine" => (1 - Math.Cos(Math.PI * t)) / 2,
            "inQuad" => t * t,
            "outQuad" => 1 - Math.Pow(1 - t, 2),
            "inOutQuad" => InOut(t, 2),
            "inCubic" => t * t * t,
            "outCubic" => 1 - Math.Pow(1 - t, 3),
            "inOutCubic" => InOut(t, 3),
            "inQuart" => Math.Pow(t, 4),
            "outQuart" => 1 - Math.Pow(1 - t, 4),
            "inOutQuart" => InOut(t, 4),
            "inQuint" => Math.Pow(t, 5),
            "outQuint" => 1 - Math.Pow(1 - t, 5),
            "inOutQuint" => InOut(t, 5),
            "inExpo" => Math.Pow(2, 10 * (t - 1)),
            "outExpo" => 1 - Math.Pow(2, -10 * t),
            "inOutExpo" => t < .5 ? Math.Pow(2, 20 * t - 10) / 2 : (2 - Math.Pow(2, -20 * t + 10)) / 2,
            "inCirc" => 1 - Math.Sqrt(1 - t * t),
            "outCirc" => Math.Sqrt(1 - Math.Pow(t - 1, 2)),
            "inOutCirc" => t < .5 ? (1 - Math.Sqrt(1 - 4 * t * t)) / 2 : (Math.Sqrt(1 - Math.Pow(-2 * t + 2, 2)) + 1) / 2,
            "inBack" => (back + 1) * t * t * t - back * t * t,
            "outBack" => 1 + (back + 1) * Math.Pow(t - 1, 3) + back * Math.Pow(t - 1, 2),
            "inOutBack" => t < .5 ? Math.Pow(2 * t, 2) * ((back * 1.525 + 1) * 2 * t - back * 1.525) / 2 : (Math.Pow(2 * t - 2,
                2) * ((back * 1.525 + 1) * (t * 2 - 2) + back * 1.525) + 2) / 2,
            "inElastic" => -Math.Pow(2, 10 * t - 10) * Math.Sin((t * 10 - 10.75) * 2 * Math.PI / 3),
            "outElastic" => Math.Pow(2, -10 * t) * Math.Sin((t * 10 - .75) * 2 * Math.PI / 3) + 1,
            "inOutElastic" => t < .5 ? -(Math.Pow(2, 20 * t - 10) * Math.Sin((20 * t - 11.125) * 2 * Math.PI / 4.5)) / 2 : Math.Pow(2,
                -20 * t + 10) * Math.Sin((20 * t - 11.125) * 2 * Math.PI / 4.5) / 2 + 1,
            "outBounce" => Bounce(t),
            "inBounce" => 1 - Bounce(1 - t),
            "inOutBounce" => t < .5 ? (1 - Bounce(1 - 2 * t)) / 2 : (1 + Bounce(2 * t - 1)) / 2,
            _ => throw new InvalidDataException($"Unsupported easing: {ease}")
        };
    }

    static double InOut(double t, int power) => t < .5 ? Math.Pow(2 * t, power) / 2 : 1 - Math.Pow(2 - 2 * t, power) / 2;
    /// <summary>2.75 分段的弹跳常数同样来自原版，四段的偏移和落点不可另行取整。</summary>
    static double Bounce(double t)
    {
        if (t < 1 / 2.75)
        {
            return 7.5625 * t * t;
        }
        if (t < 2 / 2.75)
        {
            t -= 1.5 / 2.75;
            return 7.5625 * t * t + .75;
        }
        if (t < 2.5 / 2.75)
        {
            t -= 2.25 / 2.75;
            return 7.5625 * t * t + .9375;
        }
        t -= 2.625 / 2.75;
        return 7.5625 * t * t + .984375;
    }
}

