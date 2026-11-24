namespace KuroakiGimmick.Core;

/// <summary>设备级引擎选择独立于歌曲工程；基准测试和 CI 可通过环境变量覆盖偏好。</summary>
internal static class OptimizationEngineSettings
{
    static readonly object Gate = new();
    private static ViewerSettings? options;

    internal static ViewerSettings Current
    {
        get
        {
            lock (Gate) return options ??= ViewerSettings.Load();
        }
    }

    internal static void Apply(ViewerSettings userOptions)
    {
        lock (Gate) options = userOptions;
    }

    internal static string CalculationMode
    {
        get
        {
            var configured = Environment.GetEnvironmentVariable("KG_RUST_ENGINE")?.Trim().ToLowerInvariant();
            if (configured is "off" or "auto" or "verify" or "required") return configured;
            return Current.CalculationEngine == "compatibility" ? "off" : "auto";
        }
    }
    internal static string AudioMode
    {
        get
        {
            var configured = Environment.GetEnvironmentVariable("KG_AUDIO_ENGINE")?.Trim().ToLowerInvariant();
            if (configured is "optimization" or "nvorbis") return configured;
            return Current.AudioEngine == "nvorbis" ? "nvorbis" : "optimization";
        }
    }
    internal static bool ParallelLoading => Flag("KG_PARALLEL_LOADING", Current.ParallelLoading);
    internal static bool AudioCache => Flag("KG_AUDIO_CACHE", Current.DecodedAudioCache);
    private static bool Flag(string key, bool fallback) =>
        Environment.GetEnvironmentVariable(key)?.Trim().ToLowerInvariant() switch
        { "1" or "true" or "on" => true, "0" or "false" or "off" => false, _ => fallback };
}
