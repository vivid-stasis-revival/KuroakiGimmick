using KuroakiGimmick.Graphics;

namespace KuroakiGimmick.Core;

/// <summary>Stable panel identity. The tree contains IDs and geometry only, never document state.</summary>
public static class DockPanels
{
    public static readonly string[] Viewer = ["sources", "preview", "transport", "inspector", "export", "diagnostics"];
    public static readonly string[] Editor = ["tools", "images", "text", "preview", "inspector", "timeline"];
    public static IReadOnlyList<string> For(bool editor) => editor ? Editor : Viewer;
}

public enum DockSide { Center, Left, Right, Top, Bottom }
public enum DockAxis { Horizontal, Vertical }

public abstract class DockNode;

public sealed class DockTabs : DockNode
{
    public List<string> Panels { get; } = [];
    public string Active { get; set; }
    public DockTabs(IEnumerable<string> panels, string? active = null)
    {
        Panels.AddRange(panels);
        if (Panels.Count == 0) throw new ArgumentException("A tab node needs a panel.", nameof(panels));
        Active = active != null && Panels.Contains(active) ? active : Panels[0];
    }
}

public sealed class DockSplit(DockAxis axis, float ratio, DockNode first, DockNode second) : DockNode
{
    public DockAxis Axis { get; set; } = axis;
    public float Ratio { get; set; } = DockLayout.SafeRatio(ratio);
    public DockNode First { get; set; } = first;
    public DockNode Second { get; set; } = second;
}

public sealed class DockRoot(string id, DockNode? node = null)
{
    public string Id { get; } = id;
    public DockNode? Node { get; set; } = node;
}

public readonly record struct DockPanelRect(string Window, string Panel, Rect Rect, bool Active);
public readonly record struct DockSplitterRect(string Window, Rect Rect, DockSplit Split, Rect Parent);

/// <summary>
/// Pure layout engine. A panel occurs in at most one root; every mutation prunes empty tabs and splits.
/// The main root can be temporarily empty while a panel is in a detached root.
/// </summary>
public sealed partial class DockLayout
{
    public const string MainWindow = "main";
    const int MaxDepth = 32;
    readonly HashSet<string> known;
    readonly Dictionary<string, DockRoot> roots = new(StringComparer.Ordinal);
    readonly Dictionary<string, long> activationOrder = new(StringComparer.Ordinal);
    long activationSequence;
    readonly HashSet<string> hidden = new(StringComparer.Ordinal);
    public IReadOnlyDictionary<string, DockRoot> Roots => roots;
    public IReadOnlyCollection<string> Hidden => hidden;
    public DockLayout(IEnumerable<string> panelIds, DockNode main) : this(panelIds, main, true) { }
    DockLayout(IEnumerable<string> panelIds, DockNode? main, bool validate)
    {
        known = new(panelIds, StringComparer.Ordinal);
        roots.Add(MainWindow, new(MainWindow, main));
        if (validate && !Validate()) throw new ArgumentException("Invalid initial dock layout.", nameof(main));
    }
    public static float SafeRatio(float value) => float.IsFinite(value) ? Math.Clamp(value, .1f, .9f) : .5f;
    public static DockLayout Default(bool editor)
    {
        DockNode node = editor
            ? new DockSplit(DockAxis.Vertical, .53f,
                new DockSplit(DockAxis.Horizontal, .14f,
                    new DockTabs(["tools", "images", "text"]),
                    new DockSplit(DockAxis.Horizontal, .762f, new DockTabs(["preview"]), new DockTabs(["inspector"]))),
                new DockTabs(["timeline"]))
            : new DockSplit(DockAxis.Horizontal, .14f, new DockTabs(["sources"]),
                new DockSplit(DockAxis.Horizontal, .762f,
                    new DockSplit(DockAxis.Vertical, .63f, new DockTabs(["preview"]), new DockTabs(["transport"])),
                    new DockTabs(["inspector", "export", "diagnostics"])));
        return new DockLayout(DockPanels.For(editor), node);
    }

    public bool Validate()
    {
        if (!roots.ContainsKey(MainWindow) || roots.Count > 33) return false;
        var definitions = known.Contains("timeline") ? PanelRegistry.Editor : PanelRegistry.Viewer;
        var nodes = new HashSet<DockNode>(ReferenceEqualityComparer.Instance);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var root in roots.Values)
        {
            if (root.Id.Length is 0 or > 80 || !Check(root.Node, 0)) return false;
        }
        foreach (string id in hidden)
            if (!known.Contains(id) || seen.Contains(id) || !definitions.TryGetValue(id, out var definition) ||
                !definition.CanHide) return false;
        return seen.Count + hidden.Count == known.Count;

        bool Check(DockNode? node, int depth)
        {
            if (node == null) return true;
            if (depth > MaxDepth || !nodes.Add(node)) return false;
            if (node is DockTabs tabs)
            {
                if (tabs.Panels.Count is 0 or > 64 || !tabs.Panels.Contains(tabs.Active)) return false;
                return tabs.Panels.All(p => known.Contains(p) && seen.Add(p));
            }
            if (node is DockSplit split)
                return Enum.IsDefined(split.Axis) && float.IsFinite(split.Ratio) && split.Ratio >= .1f && split.Ratio <= .9f
                    && Check(split.First, depth + 1) && Check(split.Second, depth + 1);
            return false;
        }
    }

    public string? WindowOf(string panel)
    {
        foreach (var root in roots.Values)
            if (Find(root.Node, panel) != null) return root.Id;
        return null;
    }
    public bool IsVisible(string panel) => WindowOf(panel) != null;
    public IReadOnlyList<string> PanelsInWindow(string window) =>
        roots.TryGetValue(window, out var root) && Validate() ? Enumerate(root.Node).ToArray() : [];
    public (float Width, float Height) MinimumSize(string window)
    {
        if (!roots.TryGetValue(window, out var root) || root.Node == null || !Validate()) return (240, 160);
        return Minimum(root.Node, known.Contains("timeline") ? PanelRegistry.Editor : PanelRegistry.Viewer);
    }
    public string? ActivePanel(string window)
    {
        if (!roots.TryGetValue(window, out var root)) return null;
        DockNode? node = root.Node;
        while (node is DockSplit split) node = split.First;
        return (node as DockTabs)?.Active;
    }
    public IReadOnlyList<string>? TabsFor(string panel)
    {
        foreach (var root in roots.Values)
            if (Find(root.Node, panel) is { } tabs) return tabs.Panels;
        return null;
    }
    public bool AddWindow(string id)
    {
        if (string.IsNullOrWhiteSpace(id) || id.Length > 80 || roots.Count >= 33 || roots.ContainsKey(id)) return false;
        roots.Add(id, new(id)); return true;
    }
    public bool CloseWindow(string id)
    {
        if (id == MainWindow || !roots.TryGetValue(id, out var root)) return false;
        foreach (string panel in Enumerate(root.Node).ToArray()) ReturnToMain(panel);
        roots.Remove(id); return true;
    }
    public bool ReturnToMain(string panel)
    {
        if (!known.Contains(panel)) return false;
        if (panel == "timeline") return Move(panel, MainWindow, null, DockSide.Bottom);
        string[] anchors = panel switch
        {
            "tools" or "images" or "text" => ["tools", "images", "text", "sources"],
            "export" or "diagnostics" => ["inspector", "export", "diagnostics"],
            "inspector" => ["preview", "export", "diagnostics"],
            "preview" => ["inspector", "sources", "tools"],
            "timeline" or "transport" => ["preview", "inspector"],
            _ => ["preview", "inspector"]
        };
        string? target = anchors.FirstOrDefault(id => id != panel && Find(roots[MainWindow].Node, id) != null);
        DockSide side = panel switch
        {
            "preview" when target == "inspector" => DockSide.Left,
            "preview" => DockSide.Right,
            "inspector" when target == "preview" => DockSide.Right,
            "timeline" or "transport" => DockSide.Bottom,
            "sources" => DockSide.Left,
            _ => DockSide.Center
        };
        return Move(panel, MainWindow, target, side);
    }
    public bool Hide(string panel)
    {
        var definitions = known.Contains("timeline") ? PanelRegistry.Editor : PanelRegistry.Viewer;
        if (!known.Contains(panel) || !definitions.TryGetValue(panel, out var definition) ||
            !definition.CanHide || WindowOf(panel) is not { } id) return false;
        roots[id].Node = Remove(roots[id].Node, panel);
        hidden.Add(panel);
        if (id != MainWindow && roots[id].Node == null) roots.Remove(id);
        return true;
    }
    public bool Restore(string panel) => known.Contains(panel) && hidden.Contains(panel) && Move(panel, MainWindow, null, DockSide.Center);
    public bool Move(string panel, string window, string? target, DockSide side, int tabIndex = -1)
    {
        if (!known.Contains(panel) || !roots.TryGetValue(window, out var destination) ||
            !Enum.IsDefined(side) || target == panel ||
            (target != null && Find(destination.Node, target) == null)) return false;
        string? sourceId = WindowOf(panel);
        if (sourceId == window && target == null && destination.Node != null && side == DockSide.Center) return false;
        if (sourceId != null) roots[sourceId].Node = Remove(roots[sourceId].Node, panel);
        hidden.Remove(panel);
        if (destination.Node == null) destination.Node = new DockTabs([panel]);
        else if (target == null)
        {
            var added = new DockTabs([panel]);
            var axis = side is DockSide.Top or DockSide.Bottom ? DockAxis.Vertical : DockAxis.Horizontal;
            bool before = side is DockSide.Left or DockSide.Top;
            destination.Node = new DockSplit(axis, before ? .25f : .65f,
                before ? added : destination.Node, before ? destination.Node : added);
        }
        else destination.Node = Insert(destination.Node, target, panel, side, tabIndex);
        if (sourceId != null && sourceId != MainWindow && roots[sourceId].Node == null) roots.Remove(sourceId);
        return true;
    }
    public bool Reorder(string panel, int index)
    {
        var tabs = roots.Values.Select(r => Find(r.Node, panel)).FirstOrDefault(t => t != null);
        if (tabs == null) return false;
        tabs.Panels.Remove(panel); tabs.Panels.Insert(Math.Clamp(index, 0, tabs.Panels.Count), panel);
        tabs.Active = panel; return true;
    }
    public bool Activate(string panel)
    {
        var tabs = roots.Values.Select(r => Find(r.Node, panel)).FirstOrDefault(t => t != null);
        if (tabs == null) return false;
        tabs.Active = panel;
        activationOrder[panel] = ++activationSequence;
        return true;
    }
    public bool Resize(DockSplit split, float ratio)
    {
        if (!float.IsFinite(ratio) || !roots.Values.Any(r => Contains(r.Node, split))) return false;
        split.Ratio = SafeRatio(ratio); return true;
    }

    static DockNode? Remove(DockNode? node, string panel)
    {
        if (node is DockTabs tabs)
        {
            if (!tabs.Panels.Remove(panel)) return tabs;
            if (tabs.Panels.Count == 0) return null;
            if (tabs.Active == panel) tabs.Active = tabs.Panels[0];
            return tabs;
        }
        if (node is DockSplit split)
        {
            var first = Remove(split.First, panel);
            var second = Remove(split.Second, panel);
            if (first == null) return second;
            if (second == null) return first;
            split.First = first; split.Second = second;
            return split;
        }
        return node;
    }
    static DockNode Insert(DockNode node, string target, string panel, DockSide side, int tabIndex)
    {
        if (node is DockTabs tabs)
        {
            if (!tabs.Panels.Contains(target)) return node;
            if (side == DockSide.Center)
            {
                tabs.Panels.Insert(Math.Clamp(tabIndex < 0 ? tabs.Panels.Count : tabIndex, 0, tabs.Panels.Count), panel);
                tabs.Active = panel; return tabs;
            }
            bool before = side is DockSide.Left or DockSide.Top;
            var leaf = new DockTabs([panel]);
            var axis = side is DockSide.Left or DockSide.Right ? DockAxis.Horizontal : DockAxis.Vertical;
            return new DockSplit(axis, .5f, before ? leaf : tabs, before ? tabs : leaf);
        }
        var split = (DockSplit)node;
        if (Find(split.First, target) != null) split.First = Insert(split.First, target, panel, side, tabIndex);
        else split.Second = Insert(split.Second, target, panel, side, tabIndex);
        return split;
    }
    static DockTabs? Find(DockNode? node, string panel) => node switch
    {
        DockTabs tabs => tabs.Panels.Contains(panel) ? tabs : null,
        DockSplit split => Find(split.First, panel) ?? Find(split.Second, panel),
        _ => null
    };
    static bool Contains(DockNode? node, DockNode target) => ReferenceEquals(node, target) ||
        node is DockSplit split && (Contains(split.First, target) || Contains(split.Second, target));
    static IEnumerable<string> Enumerate(DockNode? node)
    {
        if (node is DockTabs tabs) { foreach (var p in tabs.Panels) yield return p; }
        else if (node is DockSplit split)
        {
            foreach (var p in Enumerate(split.First)) yield return p;
            foreach (var p in Enumerate(split.Second)) yield return p;
        }
    }

    static (float W, float H) Minimum(DockNode node, IReadOnlyDictionary<string, PanelDefinition> definitions)
    {
        if (node is DockTabs tabs)
        {
            float w = 1, h = 1;
            foreach (string id in tabs.Panels)
                if (definitions.TryGetValue(id, out var panel))
                { w = Math.Max(w, panel.MinWidth); h = Math.Max(h, panel.MinHeight); }
            return (w, h);
        }
        var split = (DockSplit)node;
        var first = Minimum(split.First, definitions); var second = Minimum(split.Second, definitions);
        return split.Axis == DockAxis.Horizontal
            ? (first.W + second.W + 6, Math.Max(first.H, second.H))
            : (Math.Max(first.W, second.W), first.H + second.H + 6);
    }

    public void Solve(string window, Rect area, List<DockPanelRect> panels, List<DockSplitterRect> splitters)
    {
        if (!roots.TryGetValue(window, out var root) || !Validate()) return;
        var definitions = known.Contains("timeline") ? PanelRegistry.Editor : PanelRegistry.Viewer;
        void Compact(DockNode node, Rect rect)
        {
            // Compact groups span multiple real tab nodes. Remember the user's choice
            // without rewriting the saved split tree or its ratios.
            string? active = Enumerate(node).Where(activationOrder.ContainsKey)
                .OrderByDescending(id => activationOrder[id]).FirstOrDefault();
            void Visit(DockNode n)
            {
                if (n is DockTabs tabs)
                {
                    active ??= tabs.Active;
                    foreach (string id in tabs.Panels) panels.Add(new(window, id, rect, id == active));
                }
                else if (n is DockSplit split)
                { Visit(split.First); Visit(split.Second); }
            }
            Visit(node);
        }
        Walk(root.Node, area, 0);
        void Walk(DockNode? node, Rect rect, int depth)
        {
            if (node == null || depth > MaxDepth || rect.W <= 0 || rect.H <= 0) return;
            if (node is DockTabs tabs)
            {
                foreach (string p in tabs.Panels) panels.Add(new(window, p, rect, p == tabs.Active));
                return;
            }
            var split = (DockSplit)node;
            var firstMinimum = Minimum(split.First, definitions);
            var secondMinimum = Minimum(split.Second, definitions);
            if (split.Axis == DockAxis.Horizontal && rect.W < firstMinimum.W + secondMinimum.W + 6 ||
                split.Axis == DockAxis.Vertical && rect.H < firstMinimum.H + secondMinimum.H + 6)
            { Compact(node, rect); return; }
            float gap = 6, fraction = SafeRatio(split.Ratio);
            if (split.Axis == DockAxis.Horizontal)
            {
                float available = rect.W - gap;
                float at = Math.Clamp(available * fraction, firstMinimum.W, available - secondMinimum.W);
                Walk(split.First, new(rect.X, rect.Y, at, rect.H), depth + 1);
                splitters.Add(new(window, new(rect.X + at, rect.Y, gap, rect.H), split, rect));
                Walk(split.Second, new(rect.X + at + gap, rect.Y, Math.Max(0, rect.W - at - gap), rect.H), depth + 1);
            }
            else
            {
                float available = rect.H - gap;
                float at = Math.Clamp(available * fraction, firstMinimum.H, available - secondMinimum.H);
                Walk(split.First, new(rect.X, rect.Y, rect.W, at), depth + 1);
                splitters.Add(new(window, new(rect.X, rect.Y + at, rect.W, gap), split, rect));
                Walk(split.Second, new(rect.X, rect.Y + at + gap, rect.W, Math.Max(0, rect.H - at - gap)), depth + 1);
            }
        }
    }
}
