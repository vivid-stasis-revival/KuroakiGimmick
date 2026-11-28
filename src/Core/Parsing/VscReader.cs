// 读取文本谱面，以秒存储音符时刻并记录源方言和行诊断。


namespace KuroakiGimmick.Core;

/// <summary>
/// 文本音符解析器。逐行报告格式错误，保留可用音符；原文件单位转换集中在解析边界，输出统一为秒。
/// </summary>
// 自制曲加载器使用的文本谱面格式。时间与 hold 结束时刻都是毫秒，在此边界一次性转成秒。
public static class VscReader
{
    /// <summary>从 VSC 文本谱面解析数据行并记录其格式方言。</summary>
    /// <param name="path">VSC 文本文件路径。</param>
    /// <returns>含有效音符与行诊断的谱面。</returns>
    public static Chart Load(string path)
    {
        if (RustEngineBridge.TryLoadVsc(path, out var native) && native is not null)
            return native;
        return LoadOriginal(path);
    }

    // C# 解析器同时供兼容回退与 verify 差分对照使用。
    internal static Chart LoadOriginal(string path) =>
        EngineChartAdapter.Import(KenomaGMK.Core.VscReader.Load(path,
            ChartLoadLimits.Unlimited ? long.MaxValue : 128L * 1024 * 1024));
}
