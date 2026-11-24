// 关联字幕 ID 与文本文件，并根据起始拍选择生效的提示。

using System.Globalization;

namespace KuroakiGimmick.Core;

/// <summary>
/// 歌词与自定义文字轨道。声明名、文本内容和时间位置分别处理，查找失败必须可诊断。
/// </summary>
// 文本内容按拍索引；位置与外观是普通的 VSM 轨道。
public sealed class CustomText
{
    /// <summary>在指定拍生效的字幕。</summary>
    /// <param name="Beat">起始拍数。</param>
    /// <param name="Text">字幕文本。</param>
    public record Cue(double Beat, string Text);
    /// <summary>以 ID 寻址的字幕文件和提示集合。</summary>
    /// <param name="Id">字幕轨道 ID。</param>
    /// <param name="Path">字幕文件路径。</param>
    /// <param name="Cues">轨道的字幕提示。</param>
    /// <param name="SourceText">保留供编辑使用的源文本。</param>
    public sealed record Track(string Id, string Path, List<Cue> Cues, string SourceText = "")
    {
        /// <summary>二分取该拍之前最后一条 cue；第一条 cue 之前返回空串，不回绕到末尾。</summary>
        /// <param name="beat">谱面位置，单位为拍。</param>
        /// <returns>该拍已生效的字幕内容。</returns>
        public string At(double beat)
        {
            int lo = 0, hi = Cues.Count;
            while (lo < hi)
            {
                int m = (lo + hi) / 2;
                if (Cues[m].Beat <= beat)
                {
                    lo = m + 1;
                }
                else
                {
                    hi = m;
                }
            }
            return lo == 0 ? "" : Cues[lo - 1].Text;
        }
    }

    /// <summary>已加载的字幕轨道及提示内容。</summary>
    public List<Track> Tracks { get; } = [];
    static readonly HashSet<string> Kinds = new("textX textY textrot textalp textcolrgb textcolhex textscale textsep textmaxwidth textalignv textalignh".Split(' '));
    /// <summary>将字幕轨道名称拆分为属性与文本 ID。</summary>
    /// <param name="name">完整字幕属性名。</param>
    /// <param name="kind">输出属性种类。</param>
    /// <param name="id">输出字幕 ID。</param>
    /// <returns>成功解析属性名时为 true。</returns>
    public static bool TryMod(string name, out string kind, out string id)
    {
        int i = name.IndexOf('_');
        kind = i < 0 ? name : name[..i];
        id = i < 0 ? "" : name[(i + 1)..];
        return Kinds.Contains(kind);
    }

    /// <summary>未声明轨道时的默认值；legacy 单文件模式的 160/90 是 320×180 逻辑空间的中心，新式多文件模式默认 0。</summary>
    /// <param name="kind">字幕属性键。</param>
    /// <param name="legacy">是否采用旧版无 ID 文本的默认值。</param>
    /// <returns>对应的原版初始值。</returns>
    public static double Default(string kind, bool legacy = false) => kind switch
    {
        "textX" when legacy => 160,
        "textY" when legacy => 90,
        "textalp" or "textscale" or "textsep" or "textalignh" => 1,
        "textcolrgb" or "textcolhex" => 16777215,
        "textmaxwidth" => 20,
        _ => 0
    };
    /// <summary>读取工程中的字幕源并构建按拍查询的轨道。</summary>
    /// <param name="p">当前工程的路径与呈现配置。</param>
    /// <param name="chart">提供音符、演出事件及诊断的谱面。</param>
    /// <param name="edited">按字幕 ID 提供的编辑文本。</param>
    /// <param name="enabled">是否启用字幕加载。</param>
    /// <returns>解析得到的字幕轨道集合。</returns>
    public static CustomText Load(ViewerProject p, Chart chart, IReadOnlyDictionary<string, string>? edited = null, bool? enabled = null)
    {
        var result = new CustomText();
        string? root = SongFiles.Root(p);
        if (root == null)
        {
            return result;
        }
        try
        {
            if (!(enabled ?? SongFiles.Config(p, "ENABLE_TEXT")))
            {
                return result;
            }
        }
        catch (System.Text.Json.JsonException ex)
        {
            chart.Diagnostics.Add(new("config", 0, "Invalid custom configuration: " + ex.Message, true));
        }
        string difficulty = SongFiles.Difficulty(p.Chart ?? p.Gimmick ?? p.Images!);
        string legacy = SongFiles.Existing(root, difficulty + "_text.txt") ?? System.IO.Path.Combine(root, difficulty + "_text.txt"),
            prefix = difficulty + "_text_";
        var paths = File.Exists(legacy) ? new[]
        {
            legacy
        } : Directory.EnumerateFiles(root).Where(f => Path.GetFileName(f).StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            && Path.GetExtension(f).Equals(".txt", StringComparison.OrdinalIgnoreCase)).Order(StringComparer.Ordinal).ToArray();
        var sources = edited != null ? edited.Select(x => (Id: x.Key, Path: "editor-text:" + x.Key, Body: (string?)x.Value)) :
            p.TextFiles != null ? p.TextFiles.Select(x => (Id: x.Key, Path: x.Value, Body: (string?)null)) :
            paths.Select(path => (Id: path == legacy ? "" : System.IO.Path.GetFileNameWithoutExtension(path)[prefix.Length..], Path: path, Body: (string?)null));
        foreach (var source in sources.Take(ChartLoadLimits.CountLimit(4096)))
        {
            string id = source.Id, path = source.Path;
            if (source.Body == null && !File.Exists(path))
            {
                chart.Diagnostics.Add(new(path, 0, "Text file is missing.", true));
                continue;
            }
            if (ChartLoadLimits.Exceeds(source.Body == null ? new FileInfo(path).Length : System.Text.Encoding.UTF8.GetByteCount(source.Body), 4 * 1024 * 1024))
            {
                chart.Diagnostics.Add(new(path, 0, "Text file exceeds 4 MiB.", true));
                continue;
            }
            var cues = new List<Cue>();
            int line = 0;
            string body = source.Body ?? File.ReadAllText(path);
            foreach (string raw in body.TrimStart('\uFEFF').Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
            {
                line++;
                if (string.IsNullOrWhiteSpace(raw) || raw.TrimStart().StartsWith("//"))
                {
                    continue;
                }
                int comma = raw.IndexOf(',');
                if (comma < 0)
                {
                    chart.Diagnostics.Add(new(path, line, "Text row without a comma is ignored, as in the source loader."));
                    continue;
                }
                if (comma < 0 || !double.TryParse(raw[..comma], NumberStyles.Float, CultureInfo.InvariantCulture, out double beat)
                    || !double.IsFinite(beat))
                {
                    chart.Diagnostics.Add(new(path, line, "Expected beat,text; this cue was skipped.", true));
                    continue;
                }
                string text = raw[(comma + 1)..].Replace("{{", "{").Replace("}}", "}").Replace("{n}", "\n").Replace("{comma}",
                    ",").Replace("{t}", "\t");
                cues.Add(new(beat, text));
            }
            // 即使文件为空或整段无法解析也保留在编辑器中，不丢弃其源文本。
            result.Tracks.Add(new(id, path, cues.OrderBy(c => c.Beat).ToList(), body));
            chart.TextNames.Add(id);
        }
        return result;
    }

    /// <summary>检查轨道是否引用已声明的字幕。</summary>
    /// <param name="name">完整字幕属性名。</param>
    /// <returns>引用已声明轨道时为 true。</returns>
    public bool IsDeclaredMod(string name)
    {
        if (!TryMod(name, out var kind, out var id))
        {
            return false;
        }
        return Tracks.Any(t => t.Id == id || kind is "textX" or "textY" && id == t.Id + "b");
    }
}
