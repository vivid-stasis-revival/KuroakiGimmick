// 管理停靠树的结构、移动规则和可见面板矩形的求解。

using KuroakiGimmick.Graphics;

namespace KuroakiGimmick.Core;

/// <summary>面板以稳定键标识，布局树仅持有这些标识和几何信息。</summary>
public static class DockPanels
{
    /// <summary>预览工作区使用的标准面板键。</summary>
    public static readonly string[] Viewer = ["sources", "preview", "transport", "inspector", "export", "diagnostics"];
    /// <summary>编辑工作区使用的标准面板键。</summary>
    public static readonly string[] Editor = ["tools", "images", "text", "preview", "inspector", "timeline"];
    /// <summary>选择与当前工作区模式对应的面板集合。</summary>
    /// <param name="editor">为 true 时使用编辑布局，否则使用预览布局。</param>
    /// <returns>指定模式登记的面板键。</returns>
    public static IReadOnlyList<string> For(bool editor) => editor ? Editor : Viewer;
}

/// <summary>面板相对目标标签组的停靠方位。</summary>
public enum DockSide
{
    /// <summary>插入目标标签组。</summary>
    Center,
    /// <summary>在左侧分栏。</summary>
    Left,
    /// <summary>在右侧分栏。</summary>
    Right,
    /// <summary>在上方分栏。</summary>
    Top,
    /// <summary>在下方分栏。</summary>
    Bottom
}
/// <summary>布局树分割两个子区域的方向。</summary>
public enum DockAxis
{
    /// <summary>左右排列两个子区域。</summary>
    Horizontal,
    /// <summary>上下排列两个子区域。</summary>
    Vertical
}

/// <summary>停靠布局树中标签组和分割节点的公共基类。</summary>
public abstract class DockNode;

/// <summary>在同一区域中切换活动面板的标签组。</summary>
public sealed class DockTabs : DockNode
{
    /// <summary>标签组内按显示顺序排列的面板键。</summary>
    public List<string> Panels { get; } = [];
    /// <summary>当前选中并显示的面板键。</summary>
    public string Active { get; set; }
    /// <summary>从至少一个面板建立标签组并选择活动标签。</summary>
    /// <param name="panels">标签顺序中的面板键。</param>
    /// <param name="active">初始活动标签。</param>
    /// <exception cref="ArgumentException">标签集合为空。</exception>
    public DockTabs(IEnumerable<string> panels, string? active = null)
    {
        Panels.AddRange(panels);
        if (Panels.Count == 0) throw new ArgumentException("A tab node needs a panel.", nameof(panels));
        Active = active != null && Panels.Contains(active) ? active : Panels[0];
    }
}

/// <summary>沿指定轴按比例分配两个子区域的布局节点。</summary>
/// <param name="axis">子节点的排列方向。</param>
/// <param name="ratio">第一子区域的占比。</param>
/// <param name="first">起点侧的子节点。</param>
/// <param name="second">终点侧的子节点。</param>
public sealed class DockSplit(DockAxis axis, float ratio, DockNode first, DockNode second) : DockNode
{
    /// <summary>两个子区域的分割方向。</summary>
    public DockAxis Axis { get; set; } = axis;
    /// <summary>分配给第一个子区域的比例。</summary>
    public float Ratio { get; set; } = DockLayout.SafeRatio(ratio);
    /// <summary>分割轴起点一侧的子树。</summary>
    public DockNode First { get; set; } = first;
    /// <summary>分割轴终点一侧的子树。</summary>
    public DockNode Second { get; set; } = second;
}

/// <summary>主窗口或独立窗口对应的布局树根。</summary>
/// <param name="id">窗口的稳定键。</param>
/// <param name="node">窗口内容的布局根，可为空。</param>
public sealed class DockRoot(string id, DockNode? node = null)
{
    /// <summary>布局树所属窗口的稳定标识。</summary>
    public string Id { get; } = id;
    /// <summary>窗口内容的根节点，空窗口为 null。</summary>
    public DockNode? Node { get; set; } = node;
}

/// <summary>一次布局求解得到的面板区域。</summary>
/// <param name="Window">所属窗口键。</param>
/// <param name="Panel">面板键。</param>
/// <param name="Rect">布局分配的区域。</param>
/// <param name="Active">是否为活动面板。</param>
public readonly record struct DockPanelRect(string Window, string Panel, Rect Rect, bool Active);
/// <summary>可拖动的分隔条及其父区域。</summary>
/// <param name="Window">所属窗口键。</param>
/// <param name="Rect">分隔条命中区域。</param>
/// <param name="Split">要调整的分割节点。</param>
/// <param name="Parent">计算分割比例所用的父矩形。</param>
public readonly record struct DockSplitterRect(string Window, Rect Rect, DockSplit Split, Rect Parent);

/// <summary>每个面板最多属于一个根，修改后清理空标签组与分割节点。面板全部移至独立窗口时主根可以暂时为空。</summary>
public sealed partial class DockLayout
{
    /// <summary>主窗口在布局树中的保留标识。</summary>
    public const string MainWindow = "main";
    const int MaxDepth = 32;
    readonly HashSet<string> known;
    readonly Dictionary<string, DockRoot> roots = new(StringComparer.Ordinal);
    readonly Dictionary<string, long> activationOrder = new(StringComparer.Ordinal);
    long activationSequence;
    readonly HashSet<string> hidden = new(StringComparer.Ordinal);
    /// <summary>按窗口标识查找当前布局树的根。</summary>
    public IReadOnlyDictionary<string, DockRoot> Roots => roots;
    /// <summary>已登记但当前不在任何窗口中显示的面板。</summary>
    public IReadOnlyCollection<string> Hidden => hidden;
    /// <summary>用已知面板及主窗口的根节点建立布局。</summary>
    /// <param name="panelIds">全部合法面板键。</param>
    /// <param name="main">主窗口的布局根。</param>
    public DockLayout(IEnumerable<string> panelIds, DockNode main) : this(panelIds, main, true)
    {
    }
    DockLayout(IEnumerable<string> panelIds, DockNode? main, bool validate)
    {
        known = new(panelIds, StringComparer.Ordinal);
        roots.Add(MainWindow, new(MainWindow, main));
        if (validate && !Validate()) throw new ArgumentException("Invalid initial dock layout.", nameof(main));
    }
    /// <summary>将有限分割比例限制在 0.1 至 0.9，非有限输入回退为 0.5。</summary>
    /// <param name="value">候选分割比例。</param>
    /// <returns>合法范围内的比例，非有限输入返回 0.5。</returns>
    public static float SafeRatio(float value) => float.IsFinite(value) ? Math.Clamp(value, .1f, .9f) : .5f;
    /// <summary>创建预览或编辑模式的默认停靠树。</summary>
    /// <param name="editor">为 true 时使用编辑布局，否则使用预览布局。</param>
    /// <returns>该模式的新默认布局。</returns>
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

    /// <summary>检查面板唯一性、隐藏状态、树深与分割比例。</summary>
    /// <returns>所有面板的归属、节点及树形结构均合法时为 true。</returns>
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

    /// <summary>查找包含面板的窗口标识，隐藏或未知时返回 null。</summary>
    /// <param name="panel">停靠面板的稳定键。</param>
    /// <returns>所属窗口键，未显示时为 null。</returns>
    public string? WindowOf(string panel)
    {
        foreach (var root in roots.Values)
            if (Find(root.Node, panel) != null) return root.Id;
        return null;
    }
    /// <summary>检查面板是否属于任一窗口的布局树。</summary>
    /// <param name="panel">停靠面板的稳定键。</param>
    /// <returns>面板存在于某个窗口的树中时为 true。</returns>
    public bool IsVisible(string panel) => WindowOf(panel) != null;
    /// <summary>返回窗口中的面板列表，窗口未知或布局无效时返回空列表。</summary>
    /// <param name="window">布局中的窗口键。</param>
    /// <returns>该窗口的面板键，无有效布局时为空列表。</returns>
    public IReadOnlyList<string> PanelsInWindow(string window) =>
        roots.TryGetValue(window, out var root) && Validate() ? Enumerate(root.Node).ToArray() : [];
    /// <summary>根据子树约束计算最小窗口尺寸，无法求解时返回 240×160。</summary>
    /// <param name="window">布局中的窗口键。</param>
    /// <returns>最小宽高，无法求解时为 240×160。</returns>
    public (float Width, float Height) MinimumSize(string window)
    {
        if (!roots.TryGetValue(window, out var root) || root.Node == null || !Validate()) return (240, 160);
        return Minimum(root.Node, known.Contains("timeline") ? PanelRegistry.Editor : PanelRegistry.Viewer);
    }
    /// <summary>从根节点沿第一棵子树查找活动标签，无匹配时返回 null。</summary>
    /// <param name="window">布局中的窗口键。</param>
    /// <returns>首个标签组的活动面板，无有效节点时为 null。</returns>
    public string? ActivePanel(string window)
    {
        if (!roots.TryGetValue(window, out var root)) return null;
        DockNode? node = root.Node;
        while (node is DockSplit split) node = split.First;
        return (node as DockTabs)?.Active;
    }
    /// <summary>查找面板同组的全部标签，面板未显示时返回 null。</summary>
    /// <param name="panel">停靠面板的稳定键。</param>
    /// <returns>该组的有序标签，找不到面板时为 null。</returns>
    public IReadOnlyList<string>? TabsFor(string panel)
    {
        foreach (var root in roots.Values)
            if (Find(root.Node, panel) is { } tabs) return tabs.Panels;
        return null;
    }
    /// <summary>为独立窗口注册空根，标识无效、重复或超出数量限制时返回 false。</summary>
    /// <param name="id">待注册的窗口键。</param>
    /// <returns>成功加入空根时为 true。</returns>
    public bool AddWindow(string id)
    {
        if (string.IsNullOrWhiteSpace(id) || id.Length > 80 || roots.Count >= 33 || roots.ContainsKey(id)) return false;
        roots.Add(id, new(id));
        return true;
    }
    /// <summary>将独立窗口的面板迁回主窗口后移除根节点。</summary>
    /// <param name="id">要关闭的独立窗口键。</param>
    /// <returns>已移除独立窗口时为 true。</returns>
    public bool CloseWindow(string id)
    {
        if (id == MainWindow || !roots.TryGetValue(id, out var root)) return false;
        foreach (string panel in Enumerate(root.Node).ToArray()) ReturnToMain(panel);
        roots.Remove(id);
        return true;
    }
    /// <summary>根据面板角色选择主窗口中的锚点与停靠方向。</summary>
    /// <param name="panel">停靠面板的稳定键。</param>
    /// <returns>面板成功重新停靠时为 true。</returns>
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
    /// <summary>移除允许隐藏的面板，并清理因此变空的独立窗口。</summary>
    /// <param name="panel">停靠面板的稳定键。</param>
    /// <returns>面板从可见树移入隐藏集合时为 true。</returns>
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
    /// <summary>将隐藏面板重新停靠到主窗口。</summary>
    /// <param name="panel">停靠面板的稳定键。</param>
    /// <returns>隐藏面板重新加入主窗口时为 true。</returns>
    public bool Restore(string panel) => known.Contains(panel) && hidden.Contains(panel) && Move(panel, MainWindow, null, DockSide.Center);
    /// <summary>将面板移动到目标窗口，在目标位置插入标签或创建分割节点。</summary>
    /// <param name="panel">停靠面板的稳定键。</param>
    /// <param name="window">目标窗口键。</param>
    /// <param name="target">目标面板键，无目标时可为 null。</param>
    /// <param name="side">相对于目标的停靠方位。</param>
    /// <param name="tabIndex">插入标签的位置。</param>
    /// <returns>移动成功时为 true。</returns>
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
    /// <summary>将标签移到组内指定位置并激活，索引限制在当前标签范围内。</summary>
    /// <param name="panel">停靠面板的稳定键。</param>
    /// <param name="index">目标标签索引。</param>
    /// <returns>找到标签组并完成重排时为 true。</returns>
    public bool Reorder(string panel, int index)
    {
        var tabs = roots.Values.Select(r => Find(r.Node, panel)).FirstOrDefault(t => t != null);
        if (tabs == null) return false;
        tabs.Panels.Remove(panel);
        tabs.Panels.Insert(Math.Clamp(index, 0, tabs.Panels.Count), panel);
        tabs.Active = panel;
        return true;
    }
    /// <summary>设置标签组的活动面板并更新激活顺序。</summary>
    /// <param name="panel">停靠面板的稳定键。</param>
    /// <returns>找到并激活标签时为 true。</returns>
    public bool Activate(string panel)
    {
        var tabs = roots.Values.Select(r => Find(r.Node, panel)).FirstOrDefault(t => t != null);
        if (tabs == null) return false;
        tabs.Active = panel;
        activationOrder[panel] = ++activationSequence;
        return true;
    }
    /// <summary>对树中已有的分割节点应用有效比例。</summary>
    /// <param name="split">待调整的分割节点。</param>
    /// <param name="ratio">新的第一子区占比。</param>
    /// <returns>节点在当前树内且比例有限时为 true。</returns>
    public bool Resize(DockSplit split, float ratio)
    {
        if (!float.IsFinite(ratio) || !roots.Values.Any(r => Contains(r.Node, split))) return false;
        split.Ratio = SafeRatio(ratio);
        return true;
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
            split.First = first;
            split.Second = second;
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
                tabs.Active = panel;
                return tabs;
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
        if (node is DockTabs tabs)
        {
            foreach (var p in tabs.Panels) yield return p;
        }
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
                {
                    w = Math.Max(w, panel.MinWidth);
                    h = Math.Max(h, panel.MinHeight);
                }
            return (w, h);
        }
        var split = (DockSplit)node;
        var first = Minimum(split.First, definitions);
        var second = Minimum(split.Second, definitions);
        return split.Axis == DockAxis.Horizontal
            ? (first.W + second.W + 6, Math.Max(first.H, second.H))
            : (Math.Max(first.W, second.W), first.H + second.H + 6);
    }

    /// <summary>从窗口区域计算可见面板与可拖动分隔条的矩形。</summary>
    /// <param name="window">要布局的窗口键。</param>
    /// <param name="area">可用界面区域。</param>
    /// <param name="panels">接收面板布局结果的列表。</param>
    /// <param name="splitters">接收分隔条矩形的列表。</param>
    public void Solve(string window, Rect area, List<DockPanelRect> panels, List<DockSplitterRect> splitters)
    {
        if (!roots.TryGetValue(window, out var root) || !Validate()) return;
        var definitions = known.Contains("timeline") ? PanelRegistry.Editor : PanelRegistry.Viewer;
        void Compact(DockNode node, Rect rect)
        {
            // 紧凑布局可能合并多个真实标签节点；记录用户选择时保留已保存的分割树与比例。
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
                {
                    Visit(split.First);
                    Visit(split.Second);
                }
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
            {
                Compact(node, rect);
                return;
            }
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
