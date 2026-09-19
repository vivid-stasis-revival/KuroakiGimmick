using System.Text.Json;

namespace KuroakiGimmick.Core;

/// <summary>
/// 音符时间区间，Time/End 的单位是秒；Extra 保留二进制格式的扩展字段。
/// </summary>
public record Note(double Time, int Type, int Lane, double End, IReadOnlyDictionary<int, object> Extra);

