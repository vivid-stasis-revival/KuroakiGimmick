namespace KuroakiGimmick.Core;

/// <summary>从绝对歌曲时间求取原版 obj_note_rendering Step 选用的皮肤，支持倒拖。</summary>
public static class NativeNoteSkinState
{
    /// <summary>返回时间区间和 mod 阈值均匹配的首条皮肤规则。</summary>
    /// <param name="definition">当前对象的声明式演出定义。</param>
    /// <param name="time">采样时刻，单位为秒。</param>
    /// <param name="mod">按名称读取当前轨道值的函数。</param>
    /// <returns>首条匹配的规则，无匹配时为 null。</returns>
    public static GimmickSkinRule? At(GimmickDefinition? definition, double time, Func<string, double> mod) =>
        definition?.SkinRules.FirstOrDefault(rule => (rule.Mod == null || mod(rule.Mod) >= rule.Threshold)
            && (rule.Start == null || time >= rule.Start) && (rule.End == null || time <= rule.End));
    /// <summary>取得当前匹配规则的皮肤名，无匹配时为 null。</summary>
    /// <param name="definition">当前对象的声明式演出定义。</param>
    /// <param name="time">采样时刻，单位为秒。</param>
    /// <param name="mod">按名称读取当前轨道值的函数。</param>
    /// <returns>当前皮肤名，无匹配规则时为 null。</returns>
    public static string? Name(GimmickDefinition? definition, double time, Func<string, double> mod) => At(definition, time, mod)?.Skin;
}
