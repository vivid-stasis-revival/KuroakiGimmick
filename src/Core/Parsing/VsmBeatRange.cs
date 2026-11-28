using SharedRange = KenomaGMK.Core.VsmBeatRange;

namespace KuroakiGimmick.Core;

/// <summary>
/// 运行时展开与编辑器保源片段共用的拍区间语法。单位是拍，展开出的每条事件仍由 BPM map 换算成秒。
/// </summary>
/// <param name="Start">起始拍。</param>
/// <param name="End">可选的重复结束拍。</param>
/// <param name="Step">两次触发间隔的拍数。</param>
/// <param name="Parenthesized">源表达式是否使用括号。</param>
public sealed record VsmBeatRange(double Start, double? End, double Step, bool Parenthesized)
{
    /// <summary>展开出的事件条数；1e-7 容差吸收浮点除法误差，避免 0:1:0.1 这类区间少算最后一拍。</summary>
    public int Count => SharedRange.ExpandedCount(Start, End, Step);
    /// <summary>解析 start、start:end 或 start:end:step；step 必须为正且区间递增，展开条数上限一百万。</summary>
    /// <param name="token">拍数表达式文本。</param>
    /// <param name="number">解析单个数值的函数。</param>
    /// <returns>起点、可选终点、步长及括号信息。</returns>
    /// <exception cref="FormatException">拍数表达式或循环范围不符合 VSM 语法。</exception>
    public static VsmBeatRange Parse(string token, Func<string, double> number)
    {
        var range = SharedRange.Parse(token, number, ChartLoadLimits.CountLimit(1_000_000));
        return new(range.Start, range.End, range.Step, range.Parenthesized);
    }
}
