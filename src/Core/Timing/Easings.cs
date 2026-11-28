namespace KuroakiGimmick.Core;

/// <summary>保留原编辑器扩展和测试引用的类型，曲线与边界行为由公共 Core 实现。</summary>
public static class Easings
{
    /// <summary>规范化名称供谱面诊断及求值使用。</summary>
    public static string Normalize(string s) => KenomaGMK.Core.Easings.Normalize(s);
    /// <summary>检查该名称是否映射到已实现的曲线。</summary>
    public static bool IsKnown(string s) => KenomaGMK.Core.Easings.IsKnown(s);
    /// <summary>在夹取至 [0,1] 的归一化进度处采样指定曲线。</summary>
    public static double Eval(string ease, double t) => KenomaGMK.Core.Easings.Eval(ease, t);
}
