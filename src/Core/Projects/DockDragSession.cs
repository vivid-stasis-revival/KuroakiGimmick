using System.Numerics;
using KuroakiGimmick.Graphics;

namespace KuroakiGimmick.Core;

public readonly record struct DockTarget(string Window, string Panel, DockSide Side, Rect Preview);

/// <summary>Pure drag transaction. The layout is untouched until Commit, so Esc/focus loss is safe.</summary>
public sealed class DockDragSession
{
    readonly DockLayout layout;
    readonly string panel;
    readonly Vector2 start;
    public bool Dragging { get; private set; }
    public DockTarget? Target { get; private set; }
    public bool OverSource { get; private set; }
    public string SourceWindow { get; }

    public DockDragSession(DockLayout layout, string panel, Vector2 start)
    {
        this.layout = layout;
        SourceWindow = layout.WindowOf(panel) ?? throw new ArgumentException("Panel is not visible.", nameof(panel));
        this.panel = panel; this.start = start;
    }

    public void Update(string? window, Vector2 pointer, IReadOnlyList<DockPanelRect> rects)
    {
        if (!Dragging && Vector2.DistanceSquared(pointer, start) < 36) return;
        Dragging = true; Target = null; OverSource = false;
        if (window == null) return;
        foreach (var item in rects)
        {
            if (item.Window != window || !item.Active || !item.Rect.Contains(pointer.X, pointer.Y)) continue;
            if (item.Panel == panel) { OverSource = true; return; }
            var r = item.Rect;
            float edgeX = Math.Clamp(r.W * .25f, 24, 100), edgeY = Math.Clamp(r.H * .25f, 24, 100);
            DockSide side = pointer.X < r.X + edgeX ? DockSide.Left
                : pointer.X >= r.X + r.W - edgeX ? DockSide.Right
                : pointer.Y < r.Y + edgeY ? DockSide.Top
                : pointer.Y >= r.Y + r.H - edgeY ? DockSide.Bottom : DockSide.Center;
            // The visible target buttons take precedence over the broad edge regions.
            float size = Math.Min(42, Math.Min(r.W, r.H) / 5);
            float centerX = r.X + r.W / 2, centerY = r.Y + r.H / 2;
            foreach (var (zoneSide, dx, dy) in new[]
            {
                (DockSide.Center, 0, 0), (DockSide.Left, -1, 0), (DockSide.Right, 1, 0),
                (DockSide.Top, 0, -1), (DockSide.Bottom, 0, 1)
            })
                if (new Rect(centerX + dx * (size + 4) - size / 2,
                    centerY + dy * (size + 4) - size / 2, size, size).Contains(pointer.X, pointer.Y))
                { side = zoneSide; break; }
            Rect preview = side switch
            {
                DockSide.Left => new(r.X, r.Y, r.W / 2, r.H),
                DockSide.Right => new(r.X + r.W / 2, r.Y, r.W / 2, r.H),
                DockSide.Top => new(r.X, r.Y, r.W, r.H / 2),
                DockSide.Bottom => new(r.X, r.Y + r.H / 2, r.W, r.H / 2),
                _ => r
            };
            Target = new(window, item.Panel, side, preview); return;
        }
    }
    public bool Commit(Func<string> newWindowId)
    {
        if (!Dragging) return false;
        if (OverSource) { Cancel(); return false; }
        try
        {
            if (Target is { } target) return layout.Move(panel, target.Window, target.Panel, target.Side);
            string id = newWindowId();
            if (!layout.AddWindow(id)) return false;
            if (layout.Move(panel, id, null, DockSide.Center)) return true;
            layout.CloseWindow(id); return false;
        }
        finally { Cancel(); }
    }
    public void Cancel() { Dragging = false; Target = null; OverSource = false; }
}
