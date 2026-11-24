using System.Collections.ObjectModel;

namespace KuroakiGimmick.Core;

/// <summary>
/// 音符时间区间，Time/End 的单位是秒；Extra 保留二进制格式的扩展字段。
/// ModExtra 是 Custom Songs Mod 3.4.0 文本 VSC 的第五列扩展元数据：K/G 只保留键值，不擅自解释其 mod 语义。
/// </summary>
/// <param name="Time">起始时刻，单位为秒。</param>
/// <param name="Type">源谱面的音符类型编号。</param>
/// <param name="Lane">源谱面的轨道编号。</param>
/// <param name="End">结束时刻，单位为秒。</param>
/// <param name="Extra">按数值键保存的二进制扩展数据。</param>
/// <param name="ModExtra">文本 VSC 的第五列键值，未提供时为 null。</param>
public record Note(double Time, int Type, int Lane, double End, IReadOnlyDictionary<int, object> Extra,
    IReadOnlyDictionary<string, string?>? ModExtra = null)
{
    /// <summary>没有二进制扩展字段时共享的空只读字典。</summary>
    public static IReadOnlyDictionary<int, object> EmptyExtra { get; } = ReadOnlyDictionary<int, object>.Empty;
}
