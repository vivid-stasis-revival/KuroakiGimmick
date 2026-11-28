// 维护来源 mod 标识、别名及轨道的基础默认值。

using System.Text.Json;

namespace KuroakiGimmick.Core;

/// <summary>
/// 基础协议的 mod ID、默认值和缓动映射。对象扩展 ID 来自 manifest，数组顺序属于二进制格式契约，不能按字母排序。
/// </summary>
public static class ModCatalog
{
    /// <summary>基础协议的 mod 名表：下标就是二进制 ID（0..127），插入或排序都会让旧 VSB 整体错位。</summary>
    public static readonly string[] Globals = KenomaGMK.Core.ModCatalog.GlobalNames.ToArray();
    /// <summary>二进制缓动表：下标就是 VSB 里的缓动编号，同样不能排序或去重。</summary>
    public static readonly string[] BinaryEases = KenomaGMK.Core.Easings.BinaryCurveNames.ToArray();
    /// <summary>当前基础协议已登记的 mod 名称。</summary>
    public static readonly HashSet<string> Supported = KenomaGMK.Runtime.Timeline.ChartModNames.Supported;
    // 音乐控制别名仅用于执行；编辑与导出保留源名，不从无关或未知 mod 名中去掉感叹号。
    internal static string MusicControlName(string name) => KenomaGMK.Core.ChartPlaybackMap.MusicControlName(name);
    /// <summary>
    /// 把二进制 mod ID 还原成名字。0..127 查 Globals；129 起按 id-129 查对象扩展表，表位置即编号。
    /// 无法识别时返回 global_N 或 obj:mod_N 占位，保留事件让报告能指出具体编号，不丢弃未知 mod。
    /// </summary>
    /// <param name="id">二进制 mod 编号。</param>
    /// <param name="obj">来源对象名。</param>
    /// <param name="extraMods">对象额外注册的控制名。</param>
    /// <returns>解析出的 mod 名。</returns>
    public static string Decode(int id, string obj, IReadOnlyList<string>? extraMods = null)
        => KenomaGMK.Core.ModCatalog.Decode(id, obj, extraMods, Globals);

    /// <summary>
    /// 尚无事件时该 mod 的初值。回退优先级：自定义图片 → 自定义文本 → 逐轨道特例 → 名称表；
    /// 这些数值来自原版初始化，改动会让第一条 tween 的起点漂移。
    /// </summary>
    /// <param name="name">轨道名。</param>
    /// <param name="proxy">目标 proxy 编号。</param>
    /// <param name="scroll">工程滚动倍率。</param>
    /// <returns>事件首次写入前的初始值。</returns>
    public static double Default(string name, int proxy, double scroll = 3) =>
        KenomaGMK.Runtime.Timeline.ModDefaults.Default(name,proxy,scroll);
}
