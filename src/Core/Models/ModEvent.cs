using System.Text.Json;

namespace KuroakiGimmick.Core;

/// <summary>
/// 单条演出轨道事件。Beat/Duration 为拍数，Proxy=-1 表示全局，Order 保留同拍事件的作者顺序。
/// </summary>
public record ModEvent(double Beat, double Duration, string Ease, double From, double To, string Name, int Proxy, int Order, int SourceLine = 0);

