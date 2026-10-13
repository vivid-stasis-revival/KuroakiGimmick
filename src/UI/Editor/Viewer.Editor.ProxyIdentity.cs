using System.Numerics;
using KuroakiGimmick.Graphics;

namespace KuroakiGimmick.UI;

public sealed partial class Viewer
{
    const float PrGridTitleHeight = 32, PrGridHeaderHeight = 96;
    bool prGridShowIds = true;
    bool prGridShowCrop, prGridSolo, prGridSoloRendered;
    int prGridSoloTarget = -1;
    readonly List<(int Target, Rect Bounds)> prGridLabels = [];

    // The source rectangle may include a full screen of background/UI. The lane reference
    // is explicitly separate, so its bounds cannot be mistaken for the complete proxy crop.
    static Rect ProxyTrackRegion(ProxyGeometry g, double waveX = 0, double waveY = 0) => g.TrackRegion(waveX, waveY);

    /// <summary>Read-only CLI preview setup for reproducing an author's chart at a precise time.</summary>
    public void ConfigureProxyPreview(int target, bool solo, bool sourceCrop)
    {
        OpenEditor();
        if (editor == null || target < 0 || target >= EditableProxyCount)
            throw new ArgumentOutOfRangeException(nameof(target), "Choose an existing proxy ID.");
        SelectProxyFromGuide(target); prGridMode = 1; prGridSolo = solo; prGridShowCrop = sourceCrop;
    }

    void DrawProxyUnavailable(Rect viewport, int target, string reason)
    {
        float width = Math.Min(viewport.W - 16, 480);
        var box = new Rect(viewport.X + (viewport.W - width) / 2, viewport.Y + viewport.H - 38, width, 30);
        Canvas.Fill(box, Theme.Panel.Alpha(.97)); Canvas.Border(box, Theme.BorderStrong);
        Text($"P{target}: " + reason, box.X + 8, box.Y + 8, 12, Theme.Text, true, box.W - 16);
    }

    void SelectProxyFromGuide(int target)
    {
        if (editor == null || target < 0 || target >= EditableProxyCount) return;
        // A tag selects a proxy, not an old clip on another proxy. Keep the inspector consistent.
        if (selectedClip is Guid id && EditorFindClip(id)?.Proxy != target) SetClipSelection(null);
        newProxy = target; selectedWindowEvent = -1;
        imageInspector = textInspector = false;
        FocusProxyTrack(target);
    }

    void StepProxyFromGuide(int current, int direction)
    {
        int count = EditableProxyCount;
        if (count > 0) SelectProxyFromGuide((current + direction + count) % count);
    }

    static Vector2 ProxyIdentityAnchor(ProxyGeometry geometry, Rect source, ReadOnlySpan<Vector2> polygon)
    {
        if (polygon.IsEmpty) return default;
        // Stay attached to the SAME source corner throughout a rotation. Choosing the
        // smallest screen Y switches corners at zero rotation and also switches between
        // clipped endpoints whose nominal Y=0 differs by a few floating-point bits.
        if (!geometry.TryProject(new(source.X, source.Y), out var preferred))
        {
            // A corner behind the camera has no useful screen position. The visible
            // bounds provide a finite fallback independent of clipped vertex ordering.
            var min = polygon[0]; var max = polygon[0];
            foreach (var point in polygon) { min = Vector2.Min(min, point); max = Vector2.Max(max, point); }
            preferred = (min + max) / 2;
        }
        Vector2 anchor = polygon[0];
        double best = double.PositiveInfinity;
        for (int i = 0; i < polygon.Length; i++)
        {
            var a = polygon[i]; var b = polygon[(i + 1) % polygon.Length];
            double dx = (double)b.X - a.X, dy = (double)b.Y - a.Y, lengthSquared = dx * dx + dy * dy;
            double t = lengthSquared > 0 ? Math.Clamp(((preferred.X - (double)a.X) * dx +
                (preferred.Y - (double)a.Y) * dy) / lengthSquared, 0, 1) : 0;
            var point = new Vector2((float)(a.X + dx * t), (float)(a.Y + dy * t));
            double errorX = (double)point.X - preferred.X, errorY = (double)point.Y - preferred.Y;
            double distance = errorX * errorX + errorY * errorY;
            if (distance < best) { best = distance; anchor = point; }
        }
        return Vector2.Clamp(anchor, Vector2.Zero, new(320, 180));
    }

    /// <summary>Label the lane reference regions. Hidden proxies never impersonate visible tracks.
    /// Solo mode labels only the selected proxy.</summary>
    void DrawProxyIdentityTags(Rect viewport, double time, int selected)
    {
        if (!prGridShowIds) return;
        bool custom = Current.Chart.ObjectName == "obj_custom_gimmick";
        float width = 54, height = 26;
        if (viewport.W < width + 8 || viewport.H < height + 8) return;
        Span<Vector2> polygon = stackalloc Vector2[9];
        Vector2 Pixel(Vector2 p) => new(viewport.X + p.X * viewport.W / 320, viewport.Y + p.Y * viewport.H / 180);

        // Selected first so crowded/overlapping proxies cannot displace its badge.
        int count = Current.Chart.Proxies;
        for (int order = -1; order < count; order++)
        {
            int target = order < 0 ? selected : order;
            if (target < 0 || target >= count || order == selected) continue;
            if (prGridSoloRendered && target != selected) continue;
            var g = ProxyGeometry.Sample(Current.Timeline, time, target, custom);
            if (!g.Renderable || g.Alpha <= 0) continue;
            var source = ProxyTrackRegion(g, Current.Timeline.Get("shxa", time, target), Current.Timeline.Get("shya", time, target));
            if (source.W <= 0 || source.H <= 0) continue;
            {
                var mask = ProxyMask.Sample(Current.Timeline, time, target);
                float left = Math.Max(source.X, 320 * mask.Left), top = Math.Max(source.Y, 180 * mask.Top);
                float right = Math.Min(source.X + source.W, 320 * (1 - mask.Right));
                float bottom = Math.Min(source.Y + source.H, 180 * (1 - mask.Bottom));
                source = new(left, top, right - left, bottom - top);
            }
            int vertices = g.ClipRegion(source, polygon);
            if (vertices == 0) continue;
            var anchor = Pixel(ProxyIdentityAnchor(g, source, polygon[..vertices]));
            float x = Math.Clamp(anchor.X + 7, viewport.X + 4, viewport.X + viewport.W - width - 4);
            float y = Math.Clamp(anchor.Y + 18, viewport.Y + 18, viewport.Y + viewport.H - height - 4);
            Rect badge = new(x, y, width, height);
            bool Fits(Rect r) => !Overlaps(r, prGridHudRect) && !prGridLabels.Any(label => Overlaps(r, label.Bounds));
            bool placed = Fits(badge);
            // A bounded viewport lattice handles fully overlapping proxies without overlapping IDs.
            int rows = Math.Max(1, (int)((viewport.H - 24) / (height + 4)));
            int columns = Math.Max(1, (int)((viewport.W - 8) / (width + 6)));
            int startRow = Math.Clamp((int)((y - viewport.Y - 18) / (height + 4)), 0, rows - 1);
            int startColumn = Math.Clamp((int)((x - viewport.X - 4) / (width + 6)), 0, columns - 1);
            for (int offset = 1; !placed && offset <= rows * columns; offset++)
            {
                int row = (startRow + offset) % rows, column = (startColumn + offset / rows) % columns;
                float candidateX = viewport.X + 4 + column * (width + 6);
                float candidateY = viewport.Y + 18 + row * (height + 4);
                badge = new(candidateX, candidateY, width, height);
                placed = badge.X >= viewport.X + 4 && badge.X + width <= viewport.X + viewport.W - 4 &&
                    badge.Y >= viewport.Y + 4 && badge.Y + height <= viewport.Y + viewport.H - 4 && Fits(badge);
            }
            if (!placed) continue;
            bool active = target == selected, hovered = badge.Contains(mouseX, mouseY);
            var color = active || hovered ? Theme.AccentSoft : Theme.Text;
            var center = new Vector2(badge.X + width / 2, badge.Y + height / 2);
            // Only connect when both ends are on the same outside side of the HUD, so a
            // displaced label cannot draw a leader line through the selected proxy title.
            bool clear = Math.Max(anchor.X, center.X) < prGridHudRect.X ||
                Math.Min(anchor.X, center.X) > prGridHudRect.X + prGridHudRect.W ||
                Math.Max(anchor.Y, center.Y) < prGridHudRect.Y ||
                Math.Min(anchor.Y, center.Y) > prGridHudRect.Y + prGridHudRect.H;
            if (clear) Canvas.Line(anchor.X, anchor.Y, center.X, center.Y, active ? 1.5f : 1, color.Alpha(.7));
            if (hovered && !active)
                for (int i = 0; i < vertices; i++)
                {
                    var a = Pixel(polygon[i]); var b = Pixel(polygon[(i + 1) % vertices]);
                    Canvas.Line(a.X, a.Y, b.X, b.Y, 1.8f, color);
                }
            Canvas.Fill(badge, Theme.Panel.Alpha(.97));
            Canvas.Border(badge, active ? Theme.AccentSoft : Theme.BorderStrong, active ? 2 : 1);
            Text($"P{target}", badge.X + 9, badge.Y + 5, 16, color, true, width - 14);
            prGridLabels.Add((target, badge));
        }
    }
}
