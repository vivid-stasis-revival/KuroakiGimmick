namespace KuroakiGimmick.UI;

/// <summary>
/// 仅用于呈现的有限 tween。使用单调递增的 UI 时钟，绝不使用歌曲时间或渲染帧率。
/// 反向切换目标时从当前显示值起跳；长期未使用的控件 key 会过期清除。
/// </summary>
internal sealed class UiMotion
{
    /// <summary>单条 tween 的状态。三次 ease-out；Duration &lt;= 0 视为已到达目标。</summary>
    sealed class Tween
    {
        public float From, Target;
        public double Start, Duration, Seen;
        public float At(double now)
        {
            float t = Duration <= 0 ? 1 : (float)Math.Clamp((now - Start) / Duration, 0, 1);
            float u = 1 - t;
            return From + (Target - From) * (1 - u * u * u);
        }
    }
    readonly Dictionary<string, Tween> values = new(StringComparer.Ordinal);
    double now, nextSweep;
    public bool Enabled { get; private set; } = true;
    public int Count => values.Count;

    /// <summary>
    /// 每帧开始时调用一次。now 只增不减，非有限值直接忽略，时钟回拨也不会让动画倒放；
    /// enabled 为 false 时把所有在途 tween 立即收敛到目标值。每 2 秒清理一次超过 8 秒未被访问的 key。
    /// </summary>
    public void Begin(double seconds, bool enabled)
    {
        if (double.IsFinite(seconds)) now = Math.Max(now, seconds);
        Enabled = enabled;
        if (!enabled)
            foreach (var t in values.Values) { t.From = t.Target; t.Duration = 0; }
        if (now < nextSweep) return;
        foreach (string key in values.Where(p => now - p.Value.Seen > 8).Select(p => p.Key).ToArray()) values.Remove(key);
        nextSweep = now + 2;
    }
    /// <summary>
    /// 返回 key 在本帧的显示值，duration 单位为秒。首次出现时从 initial 起跳（省略则直接等于 target，不播入场动画）；
    /// 中途改目标从当前显示值续接，因此反复打断也不会跳变。非有限的 target 归 0，非有限的 duration 归 0。
    /// 每次调用都会刷新 key 的存活时间，必须逐帧调用，否则会被 Begin 清除。
    /// </summary>
    public float To(string key, float target, double duration = .16, float? initial = null)
    {
        if (!float.IsFinite(target)) target = 0;
        duration = double.IsFinite(duration) ? Math.Max(0, duration) : 0;
        if (!values.TryGetValue(key, out var tween))
        {
            float start = initial is float v && float.IsFinite(v) ? v : target;
            tween = new Tween { From = start, Target = target, Start = now, Duration = Enabled ? duration : 0 };
            values.Add(key, tween);
        }
        else if (tween.Target != target)
        {
            tween.From = tween.At(now);
            tween.Target = target;
            tween.Start = now;
            tween.Duration = Enabled ? Math.Max(0, duration) : 0;
        }
        tween.Seen = now;
        return tween.At(now);
    }
    /// <summary>0/1 可见性 tween；初值固定为 0，因此首次显示总有入场过程。</summary>
    public float Show(string key, bool visible, double duration = .16) => To(key, visible ? 1 : 0, duration, 0);
    /// <summary>不做插值，直接把 key 定在 value 上，下一次 To 会以此为起点。用于切换内容时重新起跳，而不是在两套内容之间插值。</summary>
    public void Snap(string key, float value)
    {
        if (!float.IsFinite(value)) value = 0;
        values[key] = new Tween { From = value, Target = value, Start = now, Seen = now };
    }
    /// <summary>丢弃 key 的状态；下一次 To 按"首次出现"处理，会从 initial 重新入场。</summary>
    public void Forget(string key) => values.Remove(key);
}
