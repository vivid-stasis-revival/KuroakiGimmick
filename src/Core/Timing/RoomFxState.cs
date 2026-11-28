namespace KuroakiGimmick.Core;

/// <summary>
/// 前景房间滤镜的实际启用条件，供渲染器、逐时刻报告和 CPU 回归测试共用。
/// FX_red 的 Visible 是初始房间状态，不是动画运行中的最终状态。
/// </summary>
public static class RoomFxState
{
    /// <summary>
    /// 原版把一批 0/1 mod 当布尔开关使用。tween 在边界可能得到 0.49999999999999994，
    /// 因此语义比较带极小容差；连续 shader 参数本身不做任何舍入。
    /// </summary>
    /// <param name="value">采样得到的轨道值。</param>
    /// <param name="threshold">用于判断是否启用的阈值。</param>
    /// <returns>到达阈值或落在边界容差内时为 true。</returns>
    public static bool Enabled(double value, double threshold = .5) =>
        KenomaGMK.Runtime.Effects.RoomVisibility.Enabled(value, threshold);

    /// <summary>
    /// 回退优先级：显式 VisibilityMod（非自制对象）→ 按层名的原版规则 → 房间初始 Visible。
    /// FX_red 上“没写 UseNativeColorControls”与“写了 false”不等价：旧的完整原生对象默认走颜色控制链路，
    /// 只有显式 false 才退回房间初始状态。
    /// </summary>
    /// <param name="layer">房间中的效果层。</param>
    /// <param name="customObject">是否为自定义演出对象。</param>
    /// <param name="nonBaseFxEnabled">是否启用非基础 FX。</param>
    /// <param name="nativeColorControls">是否使用原版颜色控制。</param>
    /// <param name="mod">读取当前 mod 数值的函数。</param>
    /// <returns>当前配置和轨道值允许绘制该层时为 true。</returns>
    public static bool IsForegroundActive(GameFxProfile.Layer layer, bool customObject, bool nonBaseFxEnabled, bool nativeColorControls,
        Func<string, double> mod)
    {
        return KenomaGMK.Runtime.Effects.RoomVisibility.Active(layer.Name, layer.Enabled, layer.Visible,
            layer.Depth, layer.VisibilityMod, customObject, nonBaseFxEnabled, nativeColorControls, true, mod);
    }
}
