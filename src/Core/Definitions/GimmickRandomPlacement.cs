using System.Text.Json;

namespace KuroakiGimmick.Core;

/// <summary>以回调序号为种子的确定性随机位置；拖动时间轴不会重新抽取随机数。</summary>
public sealed class GimmickRandomPlacement
{
    /// <summary>生成种子时与来源回调序号相乘的系数。</summary>
    public uint SeedMultiplier { get; set; } = 47;
    /// <summary>生成种子时加在乘积上的偏移量。</summary>
    public uint SeedOffset { get; set; }
    /// <summary>横向随机放置范围的宽度。</summary>
    public double Width { get; set; }
    /// <summary>纵向随机放置范围的高度。</summary>
    public double Height { get; set; }
    /// <summary>是否生成整数坐标。</summary>
    public bool IntegerCoordinates { get; set; } = true;
    /// <summary>是否同时随机选择色相。</summary>
    public bool RandomHue { get; set; }
}

