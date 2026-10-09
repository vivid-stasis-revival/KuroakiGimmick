namespace KuroakiGimmick.Core;

/// <summary>Lorelei 侧条的闭式运动；取自 o_lorelei_sidething，支持任意方向的时间轴定位。</summary>
public static class LoreleiSides
{
    public const double Lifetime = 1.0 / 3;
    public static bool IsCallback(string name) => name is "lr_sides_blue" or "lr_sides_red"
        or "lr_sides_rev_blue" or "lr_sides_rev_red";
    public static bool SupportsObject(string name) => name is "obj_custom_gimmick"
        or "obj_distortedfate_gimmick" or "obj_firstbreath_gimmick";
    public readonly record struct Side(double X, double Y, int Direction, int Frame, double Alpha);
    public static IEnumerable<Side> At(string name, double age)
    {
        if (!IsCallback(name) || age < 0 || age >= Lifetime) yield break;
        bool reverse = name is "lr_sides_rev_blue" or "lr_sides_rev_red";
        int frame = name is "lr_sides_red" or "lr_sides_rev_red" ? 1 : 0;
        double distance = 800 * age - 900 * age * age;
        for (int i = 0; i < 2; i++)
        {
            int direction = (i == 0 ? 1 : -1) * (reverse ? -1 : 1);
            double start = reverse ? (i == 0 ? -10 : 330) : (i == 0 ? 150 : 170);
            yield return new(start - distance * direction, 90, direction, frame, 1 - 3 * age);
        }
    }
}
