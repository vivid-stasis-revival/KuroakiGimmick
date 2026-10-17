namespace KuroakiGimmick.Core;

/// <summary>Desktop-only opt-in. Each load captures its policy so a settings change cannot alter a load halfway through.</summary>
public static class ChartLoadLimits
{
    static readonly AsyncLocal<bool?> scoped = new();
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
    public static bool Exceeds(long size, long limit) => !Unlimited && size > limit;
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
