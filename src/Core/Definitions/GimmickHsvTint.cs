namespace KuroakiGimmick.Core;

public sealed class GimmickHsvTint
{
    public GimmickScalar Hue { get; set; } = new();
    public double Saturation { get; set; } = 1;
    public double Brightness { get; set; } = 1;
}
