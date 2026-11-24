namespace KuroakiGimmick.Core;

/// <summary>Lorelei 侧条的闭式运动；取自 o_lorelei_sidething，支持任意方向的时间轴定位。</summary>
public static class LoreleiSides
{
    /// <summary>侧条可见寿命，单位为秒。</summary>
    public const double Lifetime = 1.0 / 3;
    /// <summary>检查控制名是否对应 Lorelei 侧条回调。</summary>
    /// <param name="name">候选回调名称。</param>
    /// <returns>属于 Lorelei 侧条回调时为 true。</returns>
    public static bool IsCallback(string name) => name is "lr_sides_blue" or "lr_sides_red"
        or "lr_sides_rev_blue" or "lr_sides_rev_red";
    /// <summary>检查当前对象是否支持指定侧条表现。</summary>
    /// <param name="timeline">用于采样轨道和回调的时间轴。</param>
    /// <param name="customProtocol">是否使用自定义对象协议。</param>
    /// <returns>当前对象支持侧条表现时为 true。</returns>
    public static bool Supports(Timeline timeline, bool customProtocol) => customProtocol || timeline.Native?.Data?.HasBehavior("grid-sides") == true;
    /// <summary>一次采样得到的侧条姿态。</summary>
    /// <param name="X">逻辑横坐标。</param>
    /// <param name="Y">逻辑纵坐标。</param>
    /// <param name="Direction">侧条运动方向。</param>
    /// <param name="Frame">精灵帧号。</param>
    /// <param name="Alpha">不透明度。</param>
    public readonly record struct Side(double X, double Y, int Direction, int Frame, double Alpha);
    /// <summary>按回调名与年龄枚举当前可见侧条姿态。</summary>
    /// <param name="name">侧条回调名称。</param>
    /// <param name="age">触发后经过的秒数。</param>
    /// <returns>仍在生命周期内的侧条姿态集合。</returns>
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
