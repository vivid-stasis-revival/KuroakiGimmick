using System.Text.Json;

namespace KuroakiGimmick.Core;

/// <summary>
/// 有界标量表达式：常量 + 时间/拍数项 + mod + 包参数，再应用波形、缩放和乘法。
/// 不执行脚本或反射；引用关系会在加载时检查，避免 mod 别名/逐帧函数循环递归。
/// </summary>
public sealed class GimmickScalar
{
    /// <summary>波形变换前累加的常量项。</summary>
    public double Constant { get; set; }
    /// <summary>偏移后的歌曲秒数对应的系数。</summary>
    public double TimeScale { get; set; }
    /// <summary>求值前加到歌曲时刻的秒数，也影响拍数与轨道查询。</summary>
    public double TimeOffset { get; set; }
    /// <summary>BPM 映射所得拍数对应的系数。</summary>
    public double BeatScale { get; set; }
    /// <summary>按声明顺序累加到输入的 mod 名称。</summary>
    public string[] Mods { get; set; } = [];
    /// <summary>要累加的资源包数值参数名，null 时跳过。</summary>
    public string? Parameter { get; set; }
    /// <summary>传给原生配置的分量索引，默认 -1。</summary>
    public int ParameterIndex { get; set; } = -1;
    /// <summary>对累加值应用 sin 或 cos，未指定时直接使用累加值。</summary>
    public string? Wave { get; set; }
    /// <summary>按同一时刻的指定 mod 值缩放，可省略。</summary>
    public string? MultiplyMod { get; set; }
    /// <summary>在单个 MultiplyMod 后依次相乘的轨道名。</summary>
    public string[] MultiplyMods { get; set; } = [];
    /// <summary>取整后应用的可选下限。</summary>
    public double? Minimum { get; set; }
    /// <summary>下限处理后应用的可选上限。</summary>
    public double? Maximum { get; set; }
    /// <summary>是否乘以已加 TimeOffset 的歌曲秒数。</summary>
    public bool MultiplyTime { get; set; }
    /// <summary>波形变换后应用的倍率，默认 1。</summary>
    public double Scale { get; set; } = 1;
    /// <summary>按难度指定的额外倍率，未命中时为 1。</summary>
    public Dictionary<string, double>? DifficultyScale { get; set; }
    /// <summary>可选的最终取余周期。</summary>
    public double? Wrap { get; set; }
    /// <summary>是否在所有乘法之后、限幅与取余之前向下取整。</summary>
    public bool Floor { get; set; }
    /// <summary>加载时要求资源参数严格大于此值，null 表示没有此约束。</summary>
    public double? ParameterMinimumExclusive { get; set; }
    /// <summary>
    /// time 单位为秒，拍数经 BPM map 换算。求值顺序固定：常量/秒/拍/mod/资源参数累加 →
    /// 正弦余弦 → Scale → 难度系数 → MultiplyMod → MultiplyTime → Wrap 取模。
    /// </summary>
    /// <param name="timeline">提供 BPM、mod 和原版资源参数的时间轴。</param>
    /// <param name="time">查询时刻，单位为秒。</param>
    /// <returns>按表达式依次变换、限幅并取余后的数值。</returns>
    /// <exception cref="InvalidDataException">表达式需要资源参数，但当前没有可读取的参数数据。</exception>
    public double Value(Timeline timeline, double time)
    {
        // 1. 在同一偏移时刻累加时间、拍数、轨道及资源参数。
        time += TimeOffset;
        double value = Constant + TimeScale * time + BeatScale * timeline.Bpm.Beat(time);
        foreach (string mod in Mods)
        {
            value += timeline.Get(mod, time);
        }
        if (Parameter != null)
        {
            value += timeline.Native?.ParameterNumber(Parameter,
                ParameterIndex) ?? throw new InvalidDataException("No resource parameters are loaded.");
        }
        // 2. 对累加结果应用波形，再乘以倍率、轨道值和时间因子。
        value = Wave switch
        {
            "sin" => Math.Sin(value),
            "cos" => Math.Cos(value),
            _ => value
        };
        value *= Scale;
        value *= DifficultyScale?.GetValueOrDefault(timeline.Difficulty, 1) ?? 1;
        if (MultiplyMod != null)
        {
            value *= timeline.Get(MultiplyMod, time);
        }
        foreach (string mod in MultiplyMods) value *= timeline.Get(mod, time);
        if (MultiplyTime)
        {
            value *= time;
        }
        // 3. 依次取整、限幅和取余，顺序影响最终结果。
        if (Floor) value = Math.Floor(value);
        if (Minimum is { } minimum) value = Math.Max(minimum, value);
        if (Maximum is { } maximum) value = Math.Min(maximum, value);
        return Wrap is { } period ? value % period : value;
    }
}
