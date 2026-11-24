using System.Text.Json;

namespace KuroakiGimmick.Core;

/// <summary>
/// 单条演出轨道事件。Beat/Duration 为拍数，Proxy=-1 表示全局，Order 保留同拍事件的作者顺序。
/// </summary>
/// <param name="Beat">事件起始拍。</param>
/// <param name="Duration">事件持续拍数。</param>
/// <param name="Ease">插值所用的缓动名称。</param>
/// <param name="From">插值起点。</param>
/// <param name="To">插值终点。</param>
/// <param name="Name">轨道或回调名称。</param>
/// <param name="Proxy">目标 proxy 编号，-1 表示全局。</param>
/// <param name="Order">同拍事件的源顺序。</param>
/// <param name="SourceLine">源行号，默认值为 0。</param>
public record ModEvent(double Beat, double Duration, string Ease, double From, double To, string Name, int Proxy, int Order, int SourceLine = 0);

