using System.Collections.ObjectModel;

namespace KuroakiGimmick.Core;

/// <summary>
/// 音符时间区间，Time/End 的单位是秒；Extra 保留二进制格式的扩展字段。
/// ModExtra 是 Custom Songs Mod 3.4.0 文本 VSC 的第五列扩展元数据：K/G 只保留键值，不擅自解释其 mod 语义。
/// </summary>
public record Note(double Time, int Type, int Lane, double End, IReadOnlyDictionary<int, object> Extra,
    IReadOnlyDictionary<string, string?>? ModExtra = null)
{
    public static IReadOnlyDictionary<int, object> EmptyExtra { get; } = ReadOnlyDictionary<int, object>.Empty;
}
