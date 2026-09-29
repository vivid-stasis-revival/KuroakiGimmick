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
    public static bool Enabled(double value, double threshold = .5) =>
        value > threshold || Math.Abs(value - threshold) <= 1e-9;

    /// <summary>
    /// 回退优先级：显式 VisibilityMod（非自制对象）→ 按层名的原版规则 → 房间初始 Visible。
    /// FX_red 上“没写 UseNativeColorControls”与“写了 false”不等价：旧的完整原生对象默认走颜色控制链路，
    /// 只有显式 false 才退回房间初始状态。
    /// </summary>
    public static bool IsForegroundActive(GameFxProfile.Layer layer, bool customObject, bool nonBaseFxEnabled, bool nativeColorControls,
        Func<string, double> mod)
    {
        if (!layer.Enabled)
        {
            return false;
        }
        if (layer.VisibilityMod != null && !customObject) return Enabled(mod(layer.VisibilityMod));
        return layer.Name switch
        {
            "FX_film" => Enabled(mod("fx_film")),
            "FX_edge" => customObject && nonBaseFxEnabled ? Enabled(mod("fx_edge")) : layer.Visible,
            "FX_chroma" or "FX_contrast" or "FX_glow" => layer.Visible,
            "FX_red" => customObject ? nonBaseFxEnabled && Enabled(mod("fx_red")) : nativeColorControls ? Enabled(mod("fx_red")) : layer.Visible,
            "FX_hue" or "FX_underwater" => (!customObject || nonBaseFxEnabled) && layer.Visible,
            "FX_posterize" => mod("fx_posterize_vis") != 0,
            _ => false
        };
    }
}
