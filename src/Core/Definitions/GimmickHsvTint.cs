namespace KuroakiGimmick.Core;

/// <summary>以动态色相与指定饱和度、明度合成绘制颜色。</summary>
public sealed class GimmickHsvTint
{
    /// <summary>在时间轴上计算色相的标量表达式。</summary>
    public GimmickScalar Hue { get; set; } = new();
    /// <summary>用于 HSV 着色的饱和度。</summary>
    public double Saturation { get; set; } = 1;
    /// <summary>用于 HSV 着色的明度。</summary>
    public double Brightness { get; set; } = 1;
}
