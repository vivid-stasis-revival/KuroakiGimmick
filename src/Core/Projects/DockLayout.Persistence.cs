using KuroakiGimmick.Graphics;

namespace KuroakiGimmick.Core;

/// <summary>Flat, bounded, NativeAOT-safe layout format. Child indices cannot form runtime object cycles.</summary>
public sealed class DockLayoutFile
{
    public int Version { get; set; } = 1;
    public bool Editor { get; set; }
    public List<DockWindowFile> Windows { get; set; } = [];
    public List<DockNodeFile> Nodes { get; set; } = [];
    public List<string> Hidden { get; set; } = [];
}

public sealed class DockWindowFile
{
    public string Id { get; set; } = "";
    public int Root { get; set; } = -1;
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; } = 800;
    public int Height { get; set; } = 600;
    public uint Display { get; set; }
    public double UiScale { get; set; } = 1;
}

public sealed class DockNodeFile
{
    public string Kind { get; set; } = "";
    public DockAxis Axis { get; set; }
    public float Ratio { get; set; } = .5f;
    public int First { get; set; } = -1;
    public int Second { get; set; } = -1;
    public List<string> Panels { get; set; } = [];
    public string Active { get; set; } = "";
}

public sealed partial class DockLayout
{
    public DockLayoutFile Snapshot(bool editor, IReadOnlyDictionary<string, DockWindowFile>? geometry = null)
    {
        var file = new DockLayoutFile { Editor = editor, Hidden = hidden.Order(StringComparer.Ordinal).ToList() };
        int Add(DockNode? node)
        {
            if (node == null) return -1;
            int index = file.Nodes.Count;
            var entry = new DockNodeFile(); file.Nodes.Add(entry);
            if (node is DockTabs tabs)
            { entry.Kind = "tabs"; entry.Panels = [.. tabs.Panels]; entry.Active = tabs.Active; }
            else
            {
                var split = (DockSplit)node;
                entry.Kind = "split"; entry.Axis = split.Axis; entry.Ratio = split.Ratio;
                entry.First = Add(split.First); entry.Second = Add(split.Second);
            }
            return index;
        }
        foreach (var root in roots.Values.OrderBy(r => r.Id, StringComparer.Ordinal))
        {
            var window = geometry != null && geometry.TryGetValue(root.Id, out var saved)
                ? new DockWindowFile { Id = root.Id, X = saved.X, Y = saved.Y, Width = saved.Width,
                    Height = saved.Height, Display = saved.Display, UiScale = saved.UiScale }
                : new DockWindowFile { Id = root.Id };
            window.Root = Add(root.Node); file.Windows.Add(window);
        }
        return file;
    }

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
                var first = Read(item.First, depth + 1); var second = Read(item.Second, depth + 1);
                if (first == null || second == null) throw new InvalidDataException("Empty split.");
                node = new DockSplit(item.Axis, item.Ratio, first, second);
            }
            else throw new InvalidDataException("Unknown dock node.");
            states[index] = 2; return node;
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
        { return fallback; }
    }

    /// <summary>Temporary on-screen bounds; never overwrites the saved coordinates.</summary>
    public static Rect RecoverBounds(DockWindowFile saved, IReadOnlyList<Rect> displays)
    {
        Rect display = displays.Count == 0 ? new(0, 0, 1280, 800) : displays[0];
        foreach (var candidate in displays)
            if (candidate.Contains(saved.X + 40, saved.Y + 20)) { display = candidate; break; }
        float width = Math.Clamp(saved.Width, 200, Math.Max(200, display.W));
        float height = Math.Clamp(saved.Height, 120, Math.Max(120, display.H));
        float x = Math.Clamp(saved.X, display.X, display.X + Math.Max(0, display.W - Math.Min(width, 100)));
        float y = Math.Clamp(saved.Y, display.Y, display.Y + Math.Max(0, display.H - Math.Min(height, 40)));
        return new(x, y, width, height);
    }
}
