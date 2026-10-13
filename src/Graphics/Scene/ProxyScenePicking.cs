using System.Numerics;
using KuroakiGimmick.Core;

namespace KuroakiGimmick.Graphics;

internal static class ProxyScenePicking
{
    internal enum Intent { Pass, Reserve, Pick }

    // Kept separate from SDL event unions and GPU resources so input priority can be
    // checked without a display. The caller additionally guards canvases, drags and HUDs.
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
        // CompositeField paints high IDs first; the lowest matching ID is in front.
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
