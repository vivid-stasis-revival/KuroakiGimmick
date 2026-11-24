// 在扁平的存储节点与各窗口的运行时停靠树之间转换。

using KuroakiGimmick.Graphics;

namespace KuroakiGimmick.Core;

/// <summary>适用于 NativeAOT 的有界扁平布局格式，以子节点索引表示树，恢复时检查环路。</summary>
public sealed class DockLayoutFile
{
    /// <summary>布局快照格式版本，默认 1。</summary>
    public int Version { get; set; } = 1;
    /// <summary>是否为编辑模式布局。</summary>
    public bool Editor { get; set; }
    /// <summary>窗口几何与根节点索引的列表。</summary>
    public List<DockWindowFile> Windows { get; set; } = [];
    /// <summary>可通过索引引用的扁平布局节点表。</summary>
    public List<DockNodeFile> Nodes { get; set; } = [];
    /// <summary>保存时已隐藏的面板键。</summary>
    public List<string> Hidden { get; set; } = [];
}

/// <summary>持久化窗口位置、尺寸和布局根节点的数据。</summary>
public sealed class DockWindowFile
{
    /// <summary>用于恢复窗口的稳定键。</summary>
    public string Id { get; set; } = "";
    /// <summary>根节点在布局节点表中的索引，-1 表示空窗口。</summary>
    public int Root { get; set; } = -1;
    /// <summary>保存时窗口的桌面横坐标。</summary>
    public int X { get; set; }
    /// <summary>保存时窗口的桌面纵坐标。</summary>
    public int Y { get; set; }
    /// <summary>保存的窗口宽度。</summary>
    public int Width { get; set; } = 800;
    /// <summary>保存的窗口高度。</summary>
    public int Height { get; set; } = 600;
    /// <summary>保存窗口几何时对应的显示器 ID。</summary>
    public uint Display { get; set; }
    /// <summary>该窗口保存的界面缩放倍率。</summary>
    public double UiScale { get; set; } = 1;
}

/// <summary>持久化布局树中标签组或分割节点的数据。</summary>
public sealed class DockNodeFile
{
    /// <summary>区分 tabs 标签组与 split 分割节点。</summary>
    public string Kind { get; set; } = "";
    /// <summary>分割节点排列子区域的方向。</summary>
    public DockAxis Axis { get; set; }
    /// <summary>第一个子区域分配到的比例。</summary>
    public float Ratio { get; set; } = .5f;
    /// <summary>第一个子节点索引，未设置时为 -1。</summary>
    public int First { get; set; } = -1;
    /// <summary>第二个子节点索引，未设置时为 -1。</summary>
    public int Second { get; set; } = -1;
    /// <summary>标签组中按显示顺序排列的面板键。</summary>
    public List<string> Panels { get; set; } = [];
    /// <summary>标签组当前显示的面板键。</summary>
    public string Active { get; set; } = "";
}

public sealed partial class DockLayout
{
    /// <summary>将各窗口的布局树展平为可保存的节点索引表。</summary>
    /// <param name="editor">为 true 时使用编辑布局，否则使用预览布局。</param>
    /// <param name="geometry">按窗口标识提供的几何信息。</param>
    /// <returns>可持久化的布局快照。</returns>
    public DockLayoutFile Snapshot(bool editor, IReadOnlyDictionary<string, DockWindowFile>? geometry = null)
    {
        var file = new DockLayoutFile { Editor = editor, Hidden = hidden.Order(StringComparer.Ordinal).ToList() };
        int Add(DockNode? node)
        {
            if (node == null) return -1;
            int index = file.Nodes.Count;
            var entry = new DockNodeFile();
            file.Nodes.Add(entry);
            if (node is DockTabs tabs)
            {
                entry.Kind = "tabs";
                entry.Panels = [.. tabs.Panels];
                entry.Active = tabs.Active;
            }
            else
            {
                var split = (DockSplit)node;
                entry.Kind = "split";
                entry.Axis = split.Axis;
                entry.Ratio = split.Ratio;
                entry.First = Add(split.First);
                entry.Second = Add(split.Second);
            }
            return index;
        }
        foreach (var root in roots.Values.OrderBy(r => r.Id, StringComparer.Ordinal))
        {
            var window = geometry != null && geometry.TryGetValue(root.Id, out var saved)
                ? new DockWindowFile
                {
                    Id = root.Id,
                    X = saved.X,
                    Y = saved.Y,
                    Width = saved.Width,
                    Height = saved.Height,
                    Display = saved.Display,
                    UiScale = saved.UiScale
                }
                : new DockWindowFile { Id = root.Id };
            window.Root = Add(root.Node);
            file.Windows.Add(window);
        }
        return file;
    }

    /// <summary>将隐藏面板重新停靠到主窗口。</summary>
    /// <param name="file">保存的布局快照。</param>
    /// <param name="editor">为 true 时使用编辑布局，否则使用预览布局。</param>
    /// <returns>恢复并校验后的布局；无法使用快照时采用默认布局。</returns>
    public static DockLayout Restore(DockLayoutFile? file, bool editor)
    {
        var fallback = Default(editor);
        if (file == null || file.Version != 1 || file.Editor != editor || file.Windows == null || file.Nodes == null ||
            file.Hidden == null || file.Windows.Count is < 1 or > 33 || file.Nodes.Count > 128 ||
            file.Windows.Any(w => w == null || string.IsNullOrWhiteSpace(w.Id) || w.Id.Length > 80 ||
                w.Width is < 100 or > 16000 || w.Height is < 100 or > 16000 ||
                Math.Abs((long)w.X) > 1000000 || Math.Abs((long)w.Y) > 1000000 ||
                !double.IsFinite(w.UiScale) || w.UiScale is < .5 or > 4) ||
            file.Windows.Count(w => w.Id == MainWindow) != 1) return fallback;
        var states = new byte[file.Nodes.Count];
        DockNode? Read(int index, int depth)
        {
            if (index == -1) return null;
            if (index < 0 || index >= file.Nodes.Count || depth > MaxDepth || states[index] != 0)
                throw new InvalidDataException("Invalid dock node reference.");
            states[index] = 1;
            var item = file.Nodes[index];
            if (item == null) throw new InvalidDataException("Null dock node.");
            DockNode node;
            if (item.Kind == "tabs")
            {
                if (item.Panels == null || item.Panels.Count is 0 or > 64 || item.Panels.Any(string.IsNullOrWhiteSpace) ||
                    !item.Panels.Contains(item.Active)) throw new InvalidDataException("Invalid tabs.");
                node = new DockTabs(item.Panels, item.Active);
            }
            else if (item.Kind == "split")
            {
                if (!Enum.IsDefined(item.Axis) || !float.IsFinite(item.Ratio) || item.Ratio is < .1f or > .9f)
                    throw new InvalidDataException("Invalid split.");
                var first = Read(item.First, depth + 1);
                var second = Read(item.Second, depth + 1);
                if (first == null || second == null) throw new InvalidDataException("Empty split.");
                node = new DockSplit(item.Axis, item.Ratio, first, second);
            }
            else throw new InvalidDataException("Unknown dock node.");
            states[index] = 2;
            return node;
        }
        try
        {
            var main = file.Windows.Single(w => w.Id == MainWindow);
            var layout = new DockLayout(DockPanels.For(editor), Read(main.Root, 0), false);
            foreach (var w in file.Windows.Where(w => w.Id != MainWindow))
            {
                if (!layout.AddWindow(w.Id)) return fallback;
                layout.roots[w.Id].Node = Read(w.Root, 0);
            }
            foreach (var panel in file.Hidden) layout.hidden.Add(panel);
            if (states.Any(s => s != 2) || !layout.Validate()) return fallback;
            return layout;
        }
        catch (Exception ex) when (ex is InvalidDataException or ArgumentException or InvalidOperationException)
        {
            return fallback;
        }
    }

    /// <summary>返回暂时调整到屏幕可见区域的几何范围，保留原始保存坐标。</summary>
    /// <param name="saved">保存的窗口几何。</param>
    /// <param name="displays">当前显示器的桌面范围。</param>
    /// <returns>可在当前桌面显示的窗口矩形。</returns>
    public static Rect RecoverBounds(DockWindowFile saved, IReadOnlyList<Rect> displays)
    {
        Rect display = displays.Count == 0 ? new(0, 0, 1280, 800) : displays[0];
        foreach (var candidate in displays)
            if (candidate.Contains(saved.X + 40, saved.Y + 20))
            {
                display = candidate;
                break;
            }
        float width = Math.Clamp(saved.Width, 200, Math.Max(200, display.W));
        float height = Math.Clamp(saved.Height, 120, Math.Max(120, display.H));
        float x = Math.Clamp(saved.X, display.X, display.X + Math.Max(0, display.W - Math.Min(width, 100)));
        float y = Math.Clamp(saved.Y, display.Y, display.Y + Math.Max(0, display.H - Math.Min(height, 40)));
        return new(x, y, width, height);
    }
}
