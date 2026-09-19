using System.Text.Json;
using System.Text.Json.Nodes;

namespace KuroakiGimmick.Core.Windows;

/// <summary>
/// ExtCustomGimmick v0.3.2 配置适配器。未知字段和无关的 cgmk 选项都由 DOM 持有并原样保留，不在读写时被丢弃。
/// t/easeDur/dur 单位是秒，不是 VSM 的拍。WindowMovement 索引与原生 proxy 窗口 id 是两套编号，不能互相代入。
/// </summary>
public sealed class WindowMotionConfig
{
    public const string EventsKey = "ECG_WINDOW_MOVEMENT_EVENTS";
    public const string CountKey = "ECG_WINDOW_MOVEMENT_WINDOW_COUNT";
    public const string SettingsKey = "ECG_WINDOW_MOVEMENT_SETTINGS";
    public const string BindingsKey = "ECG_PROXY_WINDOW_BINDINGS";
    public JsonObject Root { get; }
    public string? Path { get; }
    /// <summary>是否为独立的 WindowMovement 文档。独立与内嵌两种形态的事件键名不同，读写前必须先判这一项。</summary>
    public bool Standalone => Root["format"]?.ToString() == "ExtCustomGimmick.WindowMovement";
    public JsonArray? Events => Root[Standalone ? "events" : EventsKey] as JsonArray;
    public JsonArray? Bindings => Root[BindingsKey] as JsonArray;
    /// <summary>有事件或有 proxy 绑定才算有内容。其余 cgmk 选项不计入——它们不属于窗口运动。</summary>
    public bool HasContent => (Events?.Count ?? 0) > 0 || (Bindings?.Count ?? 0) > 0;
    /// <summary>窗口数取"声明值"与"事件实际寻址到的最大下标 +1"的较大者，至少为 1；声明值偏小不能让事件被静默丢弃。</summary>
    public int Count
    {
        get
        {
            int declared = Integer(Root, Standalone ? "windowCount" : CountKey, 1);
            // ReorderWindows 的 w 不是窗口下标，不参与寻址统计；用 long 相加再夹住上限，防止 int 溢出成负数。
            int addressed = Events?.OfType<JsonObject>().Where(e => Text(e, "op") != "ReorderWindows")
                .Select(e => (int)Math.Min(int.MaxValue, (long)Integer(e, "w", 0) + 1)).DefaultIfEmpty(1).Max() ?? 1;
            return Math.Max(1, Math.Max(declared, addressed));
        }
    }

    public WindowMotionConfig(JsonObject root, string? path = null) { Root = root; Path = path; }
    /// <summary>把独立的 WindowMovement 文档转写成内嵌 cgmk 形式；事件深拷贝后才移除独立格式标记，原对象不受影响。</summary>
    public WindowMotionConfig AsInlineGameConfig()
    {
        var copy = Copy();
        if (!copy.Standalone) return copy;
        copy.Root[EventsKey] = copy.Events?.DeepClone() ?? new JsonArray();
        copy.Root[CountKey] = copy.Count;
        copy.Root.Remove("format"); copy.Root.Remove("events"); copy.Root.Remove("windowCount");
        return copy;
    }
    /// <summary>整份 DOM 深拷贝，路径一并带上。编辑前必须先 Copy，否则会改到 Session 正在使用的那一份。</summary>
    public WindowMotionConfig Copy() => new((JsonObject)Root.DeepClone(), Path);
    /// <summary>按工程统一的 JSON 选项序列化，未知字段随之原样写回。</summary>
    public string Serialize() => Root.ToJsonString(ViewerProject.Json);
    /// <summary>没有配置时的空对象。它是合法可用的，不是 null 占位——上层不需要到处判空。</summary>
    public static WindowMotionConfig Empty() => new(new JsonObject());
    /// <summary>32 MiB 上限先于 JSON 解析生效，避免误选的大文件把整个 DOM 读进内存。</summary>
    public static WindowMotionConfig Parse(string text, string? path = null)
    {
        if (text.Length > 32 * 1024 * 1024) throw new InvalidDataException("Window config exceeds 32 MiB.");
        return new(JsonNode.Parse(text) as JsonObject ?? throw new InvalidDataException("Expected a window config object."), path);
    }

    /// <summary>显式指定的路径优先，其次才按难度名和目录共用名在歌曲目录里查找；查找顺序固定，不依赖文件系统枚举顺序。</summary>
    public static string? Discover(ViewerProject project)
    {
        if (project.WindowMotion != null) return project.WindowMotion;
        string? dir = SongFiles.Root(project);
        if (dir == null) return null;
        return SongFiles.Existing(dir, SongFiles.Difficulty(project.Chart ?? project.Gimmick ?? project.Images ?? "ENCORE") + "_cgmk_config.json",
            "cgmk_config.json", "window_movement.json");
    }

    /// <summary>外置文件存在但损坏时记为错误诊断并退回空配置，不悄悄改用另一份配置冒充成功。</summary>
    public static WindowMotionConfig Load(ViewerProject project, Chart chart)
    {
        string? path = Discover(project);
        if (path == null) return Empty();
        try
        {
            var config = Parse(File.ReadAllText(path), path);
            // 旧版 FILE 引用仍会被读出来用于预览，但编辑器里保留的是原配置；另存时才把事件内嵌进来。
            if (config.Events == null && config.Root["ECG_WINDOW_MOVEMENT_FILE"] is JsonValue file)
            {
                // FILE 是相对路径，基准是配置文件所在目录。
                string legacy = System.IO.Path.GetFullPath(file.ToString(), System.IO.Path.GetDirectoryName(path)!);
                var linked = Parse(File.ReadAllText(legacy), legacy);
                if (linked.Events != null)
                {
                    config.Root[EventsKey] = linked.Events.DeepClone(); config.Root[CountKey] = linked.Count;
                    chart.Diagnostics.Add(new(path, 0, "WindowMovement FILE resolved; edited copies store inline ECG events."));
                }
            }
            config.Validate(chart);
            return config;
        }
        catch (Exception e) when (e is IOException or JsonException or InvalidDataException or FormatException or OverflowException)
        {
            chart.Diagnostics.Add(new(path, 0, "WindowMovement config unavailable: " + e.Message, true));
            return Empty();
        }
    }

    /// <summary>
    /// 只写诊断，不修改也不删除任何事件：无法预览的 op 与 preset 照样保留在 DOM 里，供原样另存。
    /// 只有"非对象事件""时间非有限""超过 64 个窗口"标为错误，会让 strict 模式返回非零退出码。
    /// </summary>
    public void Validate(Chart chart)
    {
        if ((Bindings?.Count ?? 0) > 0)
            chart.Diagnostics.Add(new(Path ?? "windows", 0, "ProxyWindowBinding preview uses normal proxy geometry and the shared playfield capture. proxyMode00 and pause/FCAC/result-merge lifecycle are not simulated."));
        if (HasContent)
            chart.Diagnostics.Add(new(Path ?? "windows", 0, "Window layout is an ExtCustomGimmick source translation; native macOS/Windows compositor behavior requires platform validation. Unknown content source ids are not substituted."));
        if (Count > 64) chart.Diagnostics.Add(new(Path ?? "windows", 0, "This preview supports at most 64 addressed windows; source configuration is retained.", true));
        foreach (var pair in (Events ?? new JsonArray()).Select((e, i) => (e, i)))
        {
            if (pair.e is not JsonObject e) { chart.Diagnostics.Add(new("windows", pair.i + 1, "Window event is not an object.", true)); continue; }
            string op = Text(e, "op");
            if (!Operations.Contains(op)) chart.Diagnostics.Add(new("windows", pair.i + 1, $"Window operation '{op}' is preserved but not previewed."));
            double t = Number(e, "t");
            if (!double.IsFinite(t) || !double.IsFinite(Duration(e))) chart.Diagnostics.Add(new("windows", pair.i + 1, "Window event has non-finite timing.", true));
            if (op == "NewWindowDance" && !Presets.Contains(Text(e, "preset", "Move")))
                chart.Diagnostics.Add(new("windows", pair.i + 1, $"Window preset '{Text(e, "preset")}' is preserved, not simulated."));
        }
    }

    /// <summary>取事件数组，缺失或类型不对时就地补一个空数组。键名随独立/内嵌形态而变，别写死。</summary>
    public JsonArray EnsureEvents()
    {
        string key = Standalone ? "events" : EventsKey;
        if (Root[key] is not JsonArray) Root[key] = new JsonArray();
        return (JsonArray)Root[key]!;
    }
    /// <summary>只增不减：已有的窗口数比请求值大时保持原值，避免缩减计数把既有事件挤出寻址范围。</summary>
    public void EnsureCount(int count)
    {
        if (count is < 1 or > 64) throw new FormatException("Window count must be 1..64.");
        Root[Standalone ? "windowCount" : CountKey] = Math.Max(count, Count);
    }

    public static readonly string[] Operations = ["NewWindowDance", "WindowResize", "HideWindow", "ReorderWindows", "SetWindowContent"];
    public static readonly string[] Presets = ["Move", "Sway", "Wrap", "Ellipse", "ShakePer"];
    public static readonly string[] Eases = ["Linear", "InSine", "OutSine", "InOutSine", "InQuad", "OutQuad", "InOutQuad",
        "InCubic", "OutCubic", "InOutCubic", "InQuart", "OutQuart", "InOutQuart", "InExpo", "OutExpo", "InOutExpo", "OutBack", "OutBounce", "OutElastic"];
    public static string Text(JsonObject o, string name, string fallback = "") => o[name]?.ToString() ?? fallback;
    /// <summary>数值一律按 InvariantCulture 解析，配置文件的含义不随系统区域设置改变；解析不出就退回 fallback，不抛错。</summary>
    public static double Number(JsonObject o, string name, double fallback = 0) =>
        o[name] is JsonValue v && double.TryParse(v.ToString(), System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out double n) ? n : fallback;
    /// <summary>取整用 Floor 且非有限值退回 <paramref name="fallback"/>，避免 NaN 强转为未定义的整数。</summary>
    public static int Integer(JsonObject o, string name, int fallback = 0)
    {
        double n = Number(o, name, fallback);
        return double.IsFinite(n) && n >= int.MinValue && n <= int.MaxValue ? (int)Math.Floor(n) : fallback;
    }
    /// <summary>只认真正的 JSON 布尔值，字符串 "true" 不算；宽松解析会让写错类型的配置悄悄生效。</summary>
    public static bool Flag(JsonObject o, string name, bool fallback = false) =>
        o[name] is JsonValue v && v.TryGetValue<bool>(out bool b) ? b : fallback;
    /// <summary>事件时长（秒）：NewWindowDance 用 easeDur，其余 op 用 dur，两个字段不可互换。</summary>
    public static double Duration(JsonObject e) => Text(e, "op") == "NewWindowDance" ? Number(e, "easeDur") : Number(e, "dur");

    /// <summary>给定的 GML 运行时会无条件访问全部字段，因此连被禁用的轴也要写出来，缺字段会让原版直接报错。</summary>
    public static JsonObject NewEvent(string op, double seconds, double duration, int window)
    {
        var e = new JsonObject { ["op"] = op, ["t"] = seconds, ["w"] = window, ["overlap"] = -1 };
        switch (op)
        {
            case "NewWindowDance":
                e["preset"] = "Move"; e["same"] = "Reset"; e["x"] = .5; e["y"] = .5;
                e["ux"] = true; e["uy"] = true; e["angle"] = 0; e["uangle"] = true;
                e["ax"] = 0; e["ay"] = 0; e["uax"] = true; e["uay"] = true;
                e["speed"] = 0; e["freq"] = 0; e["period"] = .25; e["subEase"] = "Linear";
                e["easeType"] = "InOut"; e["reference"] = "Center"; e["easeDur"] = duration; e["ease"] = "OutSine";
                break;
            case "WindowResize":
                e["sx"] = 1; e["sy"] = 1; e["usx"] = true; e["usy"] = true;
                e["px"] = .5; e["py"] = .5; e["upx"] = true; e["upy"] = true;
                e["pivotMode"] = "Default"; e["anchor"] = "None"; e["dur"] = duration; e["ease"] = "OutSine";
                break;
            case "HideWindow": e["show"] = true; break;
            case "SetWindowContent": e["room"] = 0; break;
            case "ReorderWindows": e["order"] = new JsonArray(JsonValue.Create(0)); break;
            default: throw new FormatException("Unknown window operation.");
        }
        return e;
    }

    /// <summary>下标越界或缺少 settings 时给出占位标题和默认边框，绝不抛错——样式缺失不该阻断窗口预览。</summary>
    public (string Title, bool Border) Style(int index)
    {
        var windows = (Root[SettingsKey] as JsonObject)?["windows"] as JsonArray;
        var style = index >= 0 && index < (windows?.Count ?? 0) ? windows![index] as JsonObject : null;
        return style == null ? ($"Kuroaki / Window {index}", true) : (Text(style, "title", $"Window {index}"), Flag(style, "border", true));
    }
}
