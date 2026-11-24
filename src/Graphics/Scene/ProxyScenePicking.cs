using System.Numerics;
using KuroakiGimmick.Core;

namespace KuroakiGimmick.Graphics;

internal static class ProxyScenePicking
{
    internal enum Intent
    {
        Pass, Reserve, Pick
    }

    // 拾取独立于 SDL 事件联合体与 GPU 资源，以便无显示环境也能检查输入优先级；画布、拖动及 HUD 由调用方另行判断。
    internal static Intent Resolve(bool gridActive, bool forceProxy, int clicks, bool authorObjectHit)
    {
        if (!gridActive) return Intent.Pass;
        if (clicks < 2) return forceProxy ? Intent.Reserve : Intent.Pass;
        return authorObjectHit && !forceProxy ? Intent.Pass : Intent.Pick;
    }

    internal static int? Pick(Timeline timeline, double time, bool custom, int count, Vector2 logical, int? onlyProxy = null)
    {
        if (!float.IsFinite(logical.X) || !float.IsFinite(logical.Y) || logical.X < 0 || logical.X >= 320 || logical.Y < 0 || logical.Y >= 180)
            return null;
        // CompositeField 先画较大 ID，重叠时最小的匹配 ID 位于最前。
        for (int p = 0; p < count; p++)
        {
            if (onlyProxy is int only && p != only) continue;
            var geometry = ProxyGeometry.Sample(timeline, time, p, custom);
            if (!geometry.Renderable || geometry.Alpha <= 0 || !geometry.TryUnproject(logical, out var source)) continue;
            var region = geometry.TrackRegion(timeline.Get("shxa", time, p), timeline.Get("shya", time, p));
            if (!region.Contains(source.X, source.Y)) continue;
            double alpha = Math.Clamp(geometry.Alpha, 0, 1) * ProxyMask.Sample(timeline, time, p).AlphaAt(source);
            if (double.IsFinite(alpha) && alpha > .001) return p;
        }
        return null;
    }
}
