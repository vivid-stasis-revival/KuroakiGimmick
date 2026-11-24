namespace KuroakiGimmick.Core;

/// <summary>默认裁剪哨兵兼容源十进制值与导出的 float32 表示。</summary>
public static class ProxyCropDefaults
{
    /// <summary>检查裁剪值是否等于哨兵值或其单精度表示。</summary>
    /// <param name="value">候选裁剪值。</param>
    /// <param name="sentinel">源格式哨兵值。</param>
    /// <returns>与双精度或 float32 形式匹配时为 true。</returns>
    public static bool Matches(double value, double sentinel) =>
        value == sentinel || value == (double)(float)sentinel;
}
