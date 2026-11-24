// 将指针命中映射为停靠候选，释放时才提交对布局树的修改。

using System.Numerics;
using KuroakiGimmick.Graphics;

namespace KuroakiGimmick.Core;

/// <summary>拖动面板时选中的停靠目标。</summary>
/// <param name="Window">目标窗口键。</param>
/// <param name="Panel">目标面板键。</param>
/// <param name="Side">停靠方位。</param>
/// <param name="Preview">用于预览的矩形。</param>
public readonly record struct DockTarget(string Window, string Panel, DockSide Side, Rect Preview);

/// <summary>拖动期间只更新候选目标，Commit 时才修改布局，取消或失焦可直接丢弃本次操作。</summary>
public sealed class DockDragSession
{
    readonly DockLayout layout;
    readonly string panel;
    readonly Vector2 start;
    /// <summary>指针是否已越过拖动阈值。</summary>
    public bool Dragging { get; private set; }
    /// <summary>当前命中的有效停靠目标，未命中时为 null。</summary>
    public DockTarget? Target { get; private set; }
    /// <summary>指针是否仍位于源面板内。</summary>
    public bool OverSource { get; private set; }
    /// <summary>拖动开始时容纳源面板的窗口。</summary>
    public string SourceWindow { get; }

    /// <summary>记录可见面板、所属窗口与指针的起始位置。</summary>
    /// <param name="layout">当前工作区的布局树。</param>
    /// <param name="panel">停靠面板的稳定键。</param>
    /// <param name="start">按下指针时的起点。</param>
    /// <exception cref="ArgumentException">源面板未显示在任何窗口内。</exception>
    public DockDragSession(DockLayout layout, string panel, Vector2 start)
    {
        this.layout = layout;
        SourceWindow = layout.WindowOf(panel) ?? throw new ArgumentException("Panel is not visible.", nameof(panel));
        this.panel = panel;
        this.start = start;
    }

    /// <summary>越过阈值后根据指针、目标按钮与面板边界更新停靠预览。</summary>
    /// <param name="window">指针所在窗口的键，窗口外可为空。</param>
    /// <param name="pointer">当前指针位置。</param>
    /// <param name="rects">各面板在窗口中的已求解矩形。</param>
    public void Update(string? window, Vector2 pointer, IReadOnlyList<DockPanelRect> rects)
    {
        if (!Dragging && Vector2.DistanceSquared(pointer, start) < 36) return;
        Dragging = true;
        Target = null;
        OverSource = false;
        if (window == null) return;
        foreach (var item in rects)
        {
            if (item.Window != window || !item.Active || !item.Rect.Contains(pointer.X, pointer.Y)) continue;
            if (item.Panel == panel)
            {
                OverSource = true;
                return;
            }
            var r = item.Rect;
            float edgeX = Math.Clamp(r.W * .25f, 24, 100), edgeY = Math.Clamp(r.H * .25f, 24, 100);
            DockSide side = pointer.X < r.X + edgeX ? DockSide.Left
                : pointer.X >= r.X + r.W - edgeX ? DockSide.Right
                : pointer.Y < r.Y + edgeY ? DockSide.Top
                : pointer.Y >= r.Y + r.H - edgeY ? DockSide.Bottom : DockSide.Center;
            // 可见的停靠按钮优先于范围较大的边缘命中区。
            float size = Math.Min(42, Math.Min(r.W, r.H) / 5);
            float centerX = r.X + r.W / 2, centerY = r.Y + r.H / 2;
            foreach (var (zoneSide, dx, dy) in new[]
            {
                (DockSide.Center, 0, 0), (DockSide.Left, -1, 0), (DockSide.Right, 1, 0),
                (DockSide.Top, 0, -1), (DockSide.Bottom, 0, 1)
            })
                if (new Rect(centerX + dx * (size + 4) - size / 2,
                    centerY + dy * (size + 4) - size / 2, size, size).Contains(pointer.X, pointer.Y))
                {
                    side = zoneSide;
                    break;
                }
            Rect preview = side switch
            {
                DockSide.Left => new(r.X, r.Y, r.W / 2, r.H),
                DockSide.Right => new(r.X + r.W / 2, r.Y, r.W / 2, r.H),
                DockSide.Top => new(r.X, r.Y, r.W, r.H / 2),
                DockSide.Bottom => new(r.X, r.Y + r.H / 2, r.W, r.H / 2),
                _ => r
            };
            Target = new(window, item.Panel, side, preview);
            return;
        }
    }
    /// <summary>移入命中目标，或在无目标时创建新窗口；失败时清理空窗口。</summary>
    /// <param name="newWindowId">创建独立窗口标识的函数。</param>
    /// <returns>面板成功移动时为 true。</returns>
    public bool Commit(Func<string> newWindowId)
    {
        if (!Dragging) return false;
        if (OverSource)
        {
            Cancel();
            return false;
        }
        try
        {
            if (Target is { } target) return layout.Move(panel, target.Window, target.Panel, target.Side);
            string id = newWindowId();
            if (!layout.AddWindow(id)) return false;
            if (layout.Move(panel, id, null, DockSide.Center)) return true;
            layout.CloseWindow(id);
            return false;
        }
        finally
        {
            Cancel();
        }
    }
    /// <summary>清除本次拖动预览与命中状态。</summary>
    public void Cancel()
    {
        Dragging = false;
        Target = null;
        OverSource = false;
    }
}
