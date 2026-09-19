using System.Text.Json;

namespace KuroakiGimmick.Core;

/// <summary>
/// 有界标量表达式：常量 + 时间/拍数项 + mod + 包参数，再应用波形、缩放和乘法。
/// 不执行脚本或反射；引用关系会在加载时检查，避免 mod 别名/逐帧函数循环递归。
/// </summary>
public sealed class GimmickScalar
{
    public double Constant { get; set; }
    public double TimeScale { get; set; }
    public double BeatScale { get; set; }
    public string[] Mods { get; set; } = [];
    public string? Parameter { get; set; }
    public int ParameterIndex { get; set; } = -1;
    public string? Wave { get; set; }
    public string? MultiplyMod { get; set; }
    public bool MultiplyTime { get; set; }
    public double Scale { get; set; } = 1;
    public Dictionary<string, double>? DifficultyScale { get; set; }
    public double? Wrap { get; set; }
    public double? ParameterMinimumExclusive { get; set; }
    /// <summary>
    /// time 单位为秒，拍数经 BPM map 换算。求值顺序固定：常量/秒/拍/mod/资源参数累加 →
    /// 正弦余弦 → Scale → 难度系数 → MultiplyMod → MultiplyTime → Wrap 取模。
    /// </summary>
    public double Value(Timeline timeline, double time)
    {
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
        if (MultiplyTime)
        {
            value *= time;
        }
        return Wrap is { } period ? value % period : value;
    }
}
