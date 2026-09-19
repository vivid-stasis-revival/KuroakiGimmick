namespace KuroakiGimmick.Core;

/// <summary>
/// 前景房间滤镜的实际启用条件，供渲染器、逐时刻报告和 CPU 回归测试共用。
/// FX_red 的 Visible 是初始房间状态，不是动画运行中的最终状态。
/// </summary>
public static class RoomFxState
{
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
        if (layer.VisibilityMod != null && !customObject) return mod(layer.VisibilityMod) >= .5;
        return layer.Name switch
        {
            "FX_film" => mod("fx_film") >= .5,
            "FX_edge" => customObject && nonBaseFxEnabled ? mod("fx_edge") >= .5 : layer.Visible,
            "FX_chroma" or "FX_contrast" or "FX_glow" => layer.Visible,
            "FX_red" => customObject ? nonBaseFxEnabled && mod("fx_red") >= .5 : nativeColorControls ? mod("fx_red") >= .5 : layer.Visible,
            "FX_hue" or "FX_underwater" => (!customObject || nonBaseFxEnabled) && layer.Visible,
            "FX_posterize" => mod("fx_posterize_vis") != 0,
            _ => false
        };
    }
}
