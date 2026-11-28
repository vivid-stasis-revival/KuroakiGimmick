// 将演出文本解析为谱面头字段、事件及逐帧函数。



namespace KuroakiGimmick.Core;

/// <summary>
/// 演出文本解析器：展开区间、规范化缓动并保留源行号。替换 mod 不应修改音符、资源文件或作者编排顺序。
/// </summary>
public static class VsmReader
{
    /// <summary>从文件整体替换 mod 与逐帧轨道；超过 64 MiB 直接拒绝，不做部分读取。</summary>
    /// <param name="c">要替换演出事件的谱面。</param>
    /// <param name="path">VSM 文件路径。</param>
    /// <exception cref="InvalidDataException">源 VSM 大小超出当前加载限制。</exception>
    public static void ReplaceMods(Chart c, string path)
    {
        if (ChartLoadLimits.Exceeds(new FileInfo(path).Length, 64 * 1024 * 1024))
        {
            throw new InvalidDataException("VSM exceeds 64 MiB.");
        }
        // 只有能保留 C# 表示时才采用 Rust 的 VSM 解析结果；不支持的语法、原生错误或诊断交由原解析路径处理。
        if (RustEngineBridge.TryApplyVsmFile(c, path)) return;
        ReplaceModsOriginal(c, path);
    }

    /// <summary>编辑器预览直接消费内存中的修订文本；不写临时源文件覆盖原文件，也不另建一套解析器。</summary>
    /// <param name="c">要替换演出事件的谱面。</param>
    /// <param name="text">VSM 源文本。</param>
    /// <param name="source">诊断中使用的来源标识。</param>
    public static void ReplaceModsText(Chart c, string text, string source)
    {
        if (RustEngineBridge.TryApplyVsm(c, text, source)) return;
        ReplaceModsTextOriginal(c, text, source);
    }

    internal static void ReplaceModsOriginal(Chart c, string path)
    {
        var parsed = EngineChartAdapter.ModHeaders(c);
        KenomaGMK.Core.VsmReader.ReplaceMods(parsed, path,
            ChartLoadLimits.Unlimited ? long.MaxValue : 64L * 1024 * 1024,
            ChartLoadLimits.CountLimit(1_000_000));
        EngineChartAdapter.ReplaceMods(c, parsed);
    }

    internal static void ReplaceModsTextOriginal(Chart c, string text, string source)
    {
        var parsed = EngineChartAdapter.ModHeaders(c);
        KenomaGMK.Core.VsmReader.ReplaceModsText(parsed, text, source, ChartLoadLimits.CountLimit(1_000_000));
        EngineChartAdapter.ReplaceMods(c, parsed);
    }

    internal const string TrailingCommentWarning = KenomaGMK.Core.VsmReader.TrailingCommentWarning;
}
