namespace KuroakiGimmick.Core;

/// <summary>桌面平台可选的加载上限策略。每次加载捕获当前设置，进行中的加载沿用所捕获的策略。</summary>
public static class ChartLoadLimits
{
    static readonly AsyncLocal<bool?> scoped = new();
    /// <summary>当前加载上下文是否允许超过常规谱面限制。</summary>
    public static bool Unlimited
    {
        get
        {
#if KG_MOBILE || KG_SWITCH
            return false;
#else
            return scoped.Value ?? OptimizationEngineSettings.Current.UnlimitedChartSize;
#endif
        }
    }
    /// <summary>检查当前加载策略是否拒绝超过指定上限的大小。</summary>
    /// <param name="size">待检查的大小。</param>
    /// <param name="limit">常规数量或容量上限。</param>
    /// <returns>当前策略要求限制且大小超限时为 true。</returns>
    public static bool Exceeds(long size, long limit) => !Unlimited && size > limit;
    /// <summary>在无限制模式返回 int.MaxValue，否则采用指定数量上限。</summary>
    /// <param name="limit">常规数量上限。</param>
    /// <returns>常规上限或 int.MaxValue。</returns>
    public static int CountLimit(int limit) => Unlimited ? int.MaxValue : limit;
    internal static IDisposable Capture() => Override(Unlimited);
    internal static IDisposable Override(bool unlimited)
    {
        bool? previous = scoped.Value;
        scoped.Value = unlimited;
        return new Scope(previous);
    }
    sealed class Scope(bool? previous) : IDisposable
    {
        public void Dispose() => scoped.Value = previous;
    }
}
