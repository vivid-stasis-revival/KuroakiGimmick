using System.Text.Json;

namespace KuroakiGimmick.Core;

/// <summary>
/// Custom Episodes 模组的谱面内剧情（`custom_episode` gimmick）。
///
/// 谱面里写一行 <c>beat,dur,ease,_,_,custom_episode,-1</c>，到点就在谱面上浮出一个文字框，
/// 按歌曲时间自动推进；立绘、CG、转场那些属于剧情房间的指令在谱面内不可用，模组会静默跳过
/// 并写进存档目录的 _modlog.txt。编辑器能在作者试玩之前就把这些指出来，所以跳过的种类在这里
/// 全部转成诊断。
///
/// 每一步消耗的时长只取决于它自身，所以整条时间线可以一次性前缀和展开成绝对时刻。
/// 这是刻意的：预览要能任意拖动与倒放，不能依赖"上一帧演到哪儿"。
/// </summary>
public sealed class EpisodeScript
{
    /// <summary>谱面里的 gimmick 名，由 Custom Episodes 模组用 addGlobalMod 注册。</summary>
    public const string ModName = "custom_episode";

    /// <summary>谱面内文字框的默认停留时间，毫秒；顶层 <c>chart_dwell</c> 可改。</summary>
    public const double DefaultDwell = 2000;

    /// <summary>
    /// 谱面内的打字速度缺省是剧情房间的一半。谱面房间可以跑到 1000 fps，沿用原版速度
    /// 会快得看不见打字过程，所以模组把它降到 0.5（约 50 字/秒）。
    /// </summary>
    public const double DefaultSpeed = 0.5;

    /// <summary>谱面内文字框停稳的位置。剧情房间的 create_textbox() 用的是 122，两者不能混。</summary>
    public const double ChartY = 132;

    /// <summary>演完之后文字框滑回屏外用的秒数，以及再过多久整段结束。</summary>
    public const double ExitSlideSeconds = 0.8, ExitSeconds = 1;

    /// <summary>谱面内只实现了这些指令，其余一律跳过。</summary>
    static readonly HashSet<string> Supported = new(StringComparer.OrdinalIgnoreCase)
    {
        "line", "narration", "narrator", "clear", "wait", "delay",
        "se", "bgm_gain", "bgm_stop", "end", "goto", "abort",
    };

    public string Path { get; }
    public IReadOnlyList<Diagnostic> Diagnostics { get; }
    /// <summary>剧本非空才值得展开；缺剧本或剧本坏掉时这里是 false，诊断已经说明了原因。</summary>
    public bool Playable => steps.Count > 0;

    readonly List<Step> steps;
    readonly Dictionary<string, string> names;
    readonly double dwell, defaultWidth;
    readonly bool defaultWrap;

    EpisodeScript(string path, List<Step> steps, Dictionary<string, string> names,
        double dwell, bool defaultWrap, double defaultWidth, List<Diagnostic> diagnostics)
    {
        Path = path;
        this.steps = steps;
        this.names = names;
        this.dwell = dwell;
        this.defaultWrap = defaultWrap;
        this.defaultWidth = defaultWidth;
        Diagnostics = diagnostics;
    }

    /// <summary>一条剧本步骤。字段缺省值与模组一致，未知 kind 原样保留以便报告里说得出名字。</summary>
    sealed record Step(string Kind, string? Who, string? Text, string? Value, double? Speed, double? Dwell,
        double? Ms, bool? Wrap, double? Width);

    /// <summary>
    /// 读同目录的剧本。只做解析与静态诊断——按字数算停留时间要先换行，而换行要量游戏字体的宽度，
    /// 那是绘制侧才有的能力，所以时刻表留给 <see cref="Expand"/>。
    /// 谱面里没有 custom_episode 时返回 null；文件缺失或损坏返回带诊断的空剧本，不抛异常。
    /// </summary>
    public static EpisodeScript? Load(ViewerProject p, Chart chart)
    {
        if (!chart.Mods.Any(e => e.Name.Equals(ModName, StringComparison.OrdinalIgnoreCase)))
        {
            return null;
        }
        string? file = Find(p, chart);
        if (file == null)
        {
            // 谱面写了 custom_episode 却没有剧本：游戏里表现为"什么都不发生"，日志还在存档目录里。
            return new("", [], [], DefaultDwell, true, 308, [new(ModName, 0,
                "custom_episode is triggered but no story.json was found next to the chart.", true)]);
        }
        return Read(file);
    }

    /// <summary>
    /// 这首歌的剧本文件，不问谱面有没有触发过它。编辑器要在作者写下第一条 custom_episode 之前
    /// 就知道"这里能插剧情"，而那时 <see cref="Load"/> 按定义只会返回 null。
    /// </summary>
    public static string? Find(ViewerProject p, Chart chart)
    {
        string? directory = SongFiles.Root(p);
        string? source = p.Chart ?? p.Gimmick ?? p.Images ?? p.Jacket;
        string? difficulty = source == null ? null : SongFiles.Difficulty(source);
        // `!story:` 落在 Chart.Metadata（read_mods_file 把任意 !key:value 存进 modsDefinition.data）。
        return Resolve(directory, difficulty, chart.Metadata.GetValueOrDefault("story"));
    }

    /// <summary>解析一份剧本并给出静态诊断。文件损坏或没有可用步骤都只报诊断，不抛异常。</summary>
    public static EpisodeScript Read(string file)
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(file));
            var root = document.RootElement;
            var steps = ReadSteps(root);
            var names = ReadCharacterNames(root);
            double dwell = DefaultDwell, width = 308;
            bool wrap = true;
            if (root.TryGetProperty("chart_dwell", out var d) && d.TryGetDouble(out double value)) dwell = value;
            if (root.TryGetProperty("width", out var w) && w.TryGetDouble(out double wv)) width = wv;
            if (root.TryGetProperty("wrap", out var wr) && wr.ValueKind == JsonValueKind.False) wrap = false;
            var diagnostics = new List<Diagnostic>();
            if (steps.Count == 0)
            {
                diagnostics.Add(new(file, 0, "story.json has no usable lines.", true));
            }
            // 谱面内不支持的指令在游戏里是静默跳过 + 写日志，而日志在存档目录且关不掉。
            // 编辑器能在作者试玩之前就说清楚，所以这里逐种报出来。
            foreach (string kind in steps.Select(s => s.Kind).Where(k => !Supported.Contains(k))
                .Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.Ordinal))
            {
                diagnostics.Add(new(file, 0, $"'{kind}' only works in the story room; inside a chart it is skipped."));
            }
            return new(file, steps, names, dwell, wrap, width, diagnostics);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return new(file, [], [], DefaultDwell, true, 308, [new(file, 0, "Unreadable story.json: " + ex.Message, true)]);
        }
    }

    /// <summary>
    /// 按触发时刻把剧本展开成绝对时间的对白序列，交给 <see cref="NativeStoryState"/> 重放。
    /// 每次触发各自成窗：模组在新触发到来时先停掉正在播的那段，不排队。
    /// </summary>
    public NativeSequenceDefinition Expand(IReadOnlyList<double> triggers, Func<string, float> width)
    {
        var sequence = new NativeSequenceDefinition();
        for (int i = 0; i < triggers.Count; i++)
        {
            string trigger = Trigger(i);
            double end = Expand(triggers[i], trigger, width, sequence.Story);
            sequence.StoryWindows.Add(new(trigger, triggers[i], end, end + ExitSeconds, ChartY, ExitSlideSeconds));
        }
        return sequence;
    }

    /// <summary>第 <paramref name="index"/> 次触发的窗口名。与谱面里的 mod 名区分开，避免被当成一个 mod 查表。</summary>
    public static string Trigger(int index) =>
        "custom_episode#" + index.ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>
    /// 第 <paramref name="index"/> 次触发实际占用的时段，以及它是不是被下一次触发切断了。
    /// <see cref="NativeStoryState.Sample"/> 取的是最后一个已经开始的窗口，所以下一次触发一到，
    /// 上一段就当场被顶掉——模组本身也是这个行为（新触发先停掉正在播的那段，不排队）。
    /// 编辑器据此画区间：画剧本的完整长度会承诺一段根本演不完的剧情。
    /// 起点相同的两次触发不算切断彼此（那只是同一拍上的重复行），因此只看严格更晚的那些。
    /// </summary>
    public static (double Start, double End, bool Truncated) Extent(NativeSequenceDefinition sequence, int index)
    {
        var window = sequence.StoryWindows[index];
        double full = window.Destroy ?? window.Start;
        double next = sequence.StoryWindows.Select(w => w.Start).Where(s => s > window.Start)
            .DefaultIfEmpty(double.PositiveInfinity).Min();
        return (window.Start, Math.Min(full, next), next < full);
    }

    /// <summary>
    /// 把剧本展开成绝对时刻，返回最后一步结束的秒数。
    /// 一行对白占用"打字时间 + 停留时间"：打字机每秒 100×speed 个字，停留取显式 dwell，
    /// 否则是 chart_dwell 加上一份按字数估的时间（这两笔在模组里是分开累加的，不是同一笔）。
    /// </summary>
    double Expand(double start, string trigger, Func<string, float> width, List<NativeSequenceDefinition.StoryCue> cues)
    {
        double time = start;
        foreach (var step in steps)
        {
            switch (step.Kind.ToLowerInvariant())
            {
                case "line":
                case "narration":
                {
                    if (step.Text == null)
                    {
                        break;
                    }
                    double speed = step.Speed is double s && s > 0 ? s : DefaultSpeed;
                    string body = step.Text;
                    if (step.Wrap ?? defaultWrap)
                    {
                        // 停留时间按换行之后的长度算，所以换行必须先做。
                        body = string.Join('\n', NativeStoryState.Wrap(body, width, step.Width ?? defaultWidth));
                    }
                    cues.Add(new(time, trigger, Speaker(step.Who), body, speed));
                    double typing = body.Length / (100 * speed);
                    double stay = step.Dwell ?? dwell + body.Length * 40 / Math.Max(0.1, speed);
                    time += typing + Math.Max(200, stay) / 1000;
                    break;
                }
                case "narrator":
                    // 只改名字框，不产生新的一句；下一句对白会带着这个名字出现。
                    break;
                case "clear":
                    cues.Add(new(time, trigger, "", "", DefaultSpeed));
                    break;
                case "wait":
                    // 谱面内不等按键——歌还在放，等按键就卡住了，所以退化成一次停顿。
                    time += Math.Max(0, step.Ms ?? dwell) / 1000;
                    break;
                case "delay":
                    time += Math.Max(0, step.Ms ?? 0) / 1000;
                    break;
                case "se":
                case "bgm_gain":
                case "bgm_stop":
                    // 有听觉效果但不占时间，同一帧继续走下一步。
                    break;
                case "end":
                case "goto":
                case "abort":
                    return time;
                default:
                    // 解析时已经报过诊断，这里只是不占时间地跳过。
                    break;
            }
        }
        return time;
    }

    string Speaker(string? who) =>
        who == null || who.Length == 0 ? "" : names.GetValueOrDefault(who, who);

    static List<Step> ReadSteps(JsonElement root)
    {
        var steps = new List<Step>();
        if (!root.TryGetProperty("lines", out var lines) || lines.ValueKind != JsonValueKind.Array)
        {
            return steps;
        }
        foreach (var line in lines.EnumerateArray())
        {
            if (line.ValueKind != JsonValueKind.Object)
            {
                continue;
            }
            // kind 省略即 line，与模组的 mod_cs_chart_step 一致。
            steps.Add(new(Text(line, "kind") ?? "line", Text(line, "who"), Text(line, "text"), Text(line, "value"),
                Number(line, "speed"), Number(line, "dwell"), Number(line, "ms"), Flag(line, "wrap"), Number(line, "width")));
        }
        return steps;
    }

    static Dictionary<string, string> ReadCharacterNames(JsonElement root)
    {
        var names = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!root.TryGetProperty("characters", out var characters) || characters.ValueKind != JsonValueKind.Object)
        {
            return names;
        }
        foreach (var character in characters.EnumerateObject())
        {
            // display_name 缺省就用 key 本身，和模组一致。
            names[character.Name] = character.Value.ValueKind == JsonValueKind.Object
                ? Text(character.Value, "display_name") ?? character.Name
                : character.Name;
        }
        return names;
    }

    static string? Text(JsonElement o, string name) =>
        o.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    static double? Number(JsonElement o, string name) =>
        o.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out double d) ? d : null;

    static bool? Flag(JsonElement o, string name) =>
        o.TryGetProperty(name, out var v) && v.ValueKind is JsonValueKind.True or JsonValueKind.False ? v.GetBoolean() : null;

    /// <summary>`!story:` 覆盖同目录的 story.json；两者都没有才算缺剧本。</summary>
    static string? Resolve(string? directory, string? difficulty, string? overridePath)
    {
        if (overridePath is { Length: > 0 })
        {
            string candidate = System.IO.Path.IsPathRooted(overridePath) || directory == null
                ? overridePath
                : System.IO.Path.Combine(directory, overridePath);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }
        if (directory == null || !Directory.Exists(directory))
        {
            return null;
        }
        return difficulty is { Length: > 0 }
            ? SongFiles.Existing(directory, difficulty + "_story.json", "story.json")
            : SongFiles.Existing(directory, "story.json");
    }
}
