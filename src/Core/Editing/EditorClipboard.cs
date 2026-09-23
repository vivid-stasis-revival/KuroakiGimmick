using System.Text.Json;
using System.Text.Json.Nodes;

namespace KuroakiGimmick.Core.Editing;

/// <summary>
/// 选择集的剪贴板文本格式。载荷是纯文本 JSON，因此两个编辑器实例之间、甚至通过聊天工具转发都能粘贴。
/// 事件以 VSM 源行的形式携带：与源文件共用同一套格式和解析器，贴回来的行必定可解析，
/// 也因此可以直接从文本编辑器里贴一段裸 VSM 事件行进来。
/// 图片动画在模型里本身就是 VSM 事件，不需要另一套表示。
/// </summary>
public static class EditorClipboard
{
    /// <summary>信封标记与版本。读取时版本不符就当作不是本程序的内容，宁可不粘贴也不猜结构。</summary>
    public const string Marker = "kuroaki";
    public const string Format = "clipboard/1";
    /// <summary>剪贴板文本上限。超过就拒绝，避免把一份几十兆的粘贴内容整个解析进编辑历史。</summary>
    public const int MaxLength = 1024 * 1024;
    const int MaxItems = 20000;

    /// <summary><c>Beat</c> 是这批内容的原始起点，粘贴时整体按插入点平移，组内相对间距原样保留。</summary>
    public sealed record Payload(double Beat, VsmDocument.Clip[] Events, JsonObject[] Windows)
    {
        public int Count => Events.Length + Windows.Length;
    }

    /// <summary>写出信封。事件按拍排序，让人肉阅读和 diff 时顺序稳定。</summary>
    public static string Write(IEnumerable<VsmDocument.Clip> events, IEnumerable<JsonObject> windows)
    {
        var clips = events.OrderBy(c => c.Beat).ThenBy(c => c.Name, StringComparer.Ordinal).ToArray();
        var windowEvents = windows.ToArray();
        if (clips.Length + windowEvents.Length == 0)
        {
            return "";
        }
        var root = new JsonObject
        {
            [Marker] = Format,
            ["beat"] = clips.Length > 0 ? clips.Min(c => c.Beat) : 0,
            ["events"] = new JsonArray([.. clips.Select(c => (JsonNode)VsmDocument.Compose(c))])
        };
        if (windowEvents.Length > 0)
        {
            root["windows"] = new JsonArray([.. windowEvents.Select(w => w.DeepClone())]);
        }
        return root.ToJsonString(new JsonSerializerOptions
        {
            WriteIndented = true
        });
    }

    /// <summary>
    /// 读取剪贴板文本。优先按信封解析；不是信封时退回当作裸 VSM 事件行，这样从别处复制的一段源码也能直接贴进来。
    /// 任何解析失败都返回 null，由调用方给出提示，不抛到 UI 循环里。
    /// </summary>
    public static Payload? Read(string? text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > MaxLength)
        {
            return null;
        }
        string trimmed = text.Trim();
        return trimmed.StartsWith('{') ? ReadEnvelope(trimmed) : ReadEvents(trimmed);
    }

    static Payload? ReadEnvelope(string text)
    {
        try
        {
            if (JsonNode.Parse(text) is not JsonObject root
                || root[Marker]?.GetValue<string>() != Format)
            {
                return null;
            }
            var events = new List<VsmDocument.Clip>();
            if (root["events"] is JsonArray rows)
            {
                // 逐行走一遍真正的 VSM 解析器；解析不出片段的行直接丢弃，不猜它想表达什么。
                var lines = rows.Select(r => r?.GetValue<string>()).Where(s => !string.IsNullOrWhiteSpace(s)).Take(MaxItems);
                events.AddRange(VsmDocument.FromText(string.Join('\n', lines)).Clips);
            }
            var windows = root["windows"] is JsonArray list
                ? list.OfType<JsonObject>().Take(MaxItems).Select(w => (JsonObject)w.DeepClone()).ToArray()
                : [];
            if (events.Count + windows.Length == 0)
            {
                return null;
            }
            double beat = root["beat"]?.GetValue<double>() ?? (events.Count > 0 ? events.Min(c => c.Beat) : 0);
            return new(double.IsFinite(beat) ? beat : 0, [.. events], windows);
        }
        catch (Exception ex) when (ex is JsonException or FormatException or InvalidOperationException or OverflowException)
        {
            return null;
        }
    }

    static Payload? ReadEvents(string text)
    {
        try
        {
            var clips = VsmDocument.FromText(text).Clips.Take(MaxItems).ToArray();
            return clips.Length == 0 ? null : new(clips.Min(c => c.Beat), clips, []);
        }
        catch (Exception ex) when (ex is FormatException or InvalidDataException or OverflowException)
        {
            return null;
        }
    }
}
