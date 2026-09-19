using System.Text.Json;

namespace KuroakiGimmick.Core;

/// <summary>以回调序号为种子的确定性随机位置；拖动时间轴不会重新抽取随机数。</summary>
public sealed class GimmickRandomPlacement
{
    public uint SeedMultiplier { get; set; } = 47;
    public uint SeedOffset { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
    public bool IntegerCoordinates { get; set; } = true;
    public bool RandomHue { get; set; }
}

