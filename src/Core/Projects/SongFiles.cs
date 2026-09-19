using System.Globalization;
using System.Text.Json;

namespace KuroakiGimmick.Core;

/// <summary>
/// 同目录文件关联策略。显式文件优先，固定候选顺序保证重复打开一致；音频存在多个导出候选时不猜测其中一个。
/// </summary>
public static class SongFiles
{
    /// <summary>从文件名取难度标识；前导下划线形式取第一段，例如 _ENCORE_x 得到 ENCORE。</summary>
    public static string Difficulty(string file)
    {
        string name = Path.GetFileNameWithoutExtension(file);
        return name.StartsWith('_') ? name.TrimStart('_').Split('_')[0] : name;
    }

    /// <summary>歌曲目录：取谱面、VSM、VSP、jacket 中第一个已知路径的所在目录，同目录关联都以它为基准。</summary>
    public static string? Root(ViewerProject p) => Path.GetDirectoryName(p.Chart ?? p.Gimmick ?? p.Images ?? p.Jacket);
    /// <summary>按 <paramref name="names"/> 给出的顺序取第一个存在的文件；精确匹配优先，再按 Ordinal 排序后做大小写无关匹配，保证跨平台重复打开结果一致。</summary>
    public static string? Existing(string dir, params string[] names)
    {
        var exact = names.Select(n => Path.Combine(dir, n)).FirstOrDefault(File.Exists);
        if (exact != null)
        {
            return exact;
        }
        if (!Directory.Exists(dir))
        {
            return null;
        }
        var files = Directory.EnumerateFiles(dir).Order(StringComparer.Ordinal).ToArray();
        return names.Select(n => files.FirstOrDefault(f => Path.GetFileName(f).Equals(n,
            StringComparison.OrdinalIgnoreCase))).FirstOrDefault(f => f != null);
    }

    /// <summary>把谱面/VSM/VSP 路径或歌曲目录展开成工程；目录形式按固定难度候选顺序挑选，不依赖文件系统枚举顺序。</summary>
    public static ViewerProject Open(string path)
    {
        path = Path.GetFullPath(path);
        if (Directory.Exists(path))
        {
            string? chosen = null;
            // 固定的难度优先级，保证同一目录每次打开都落到同一个谱面。
            foreach (string candidate in new[]
            {
                "ENCORE",
                "FINALE",
                "MIDDLE",
                "OPENING",
                "GLOBAL"
            })
            {
                if ((chosen = Existing(path, candidate + ".vsb", candidate + ".vsc", candidate + ".vsm")) != null)
                {
                    break;
                }
            }
            chosen ??= Directory.EnumerateFiles(path)
                .Where(p => Path.GetExtension(p).ToLowerInvariant() is ".vsb" or ".vsc" or ".vsm").Order().FirstOrDefault();
            path = chosen ?? throw new FileNotFoundException("This folder contains no .vsb, .vsc or .vsm.");
        }
        string ext = Path.GetExtension(path).ToLowerInvariant();
        if (ext is not (".vsb" or ".vsc" or ".vsm" or ".vsp"))
        {
            throw new InvalidDataException("Open a .vsb, .vsc, .vsm, .vsp, song folder or .sgv.json project.");
        }
        string dir = Path.GetDirectoryName(path)!, difficulty = Difficulty(path);
        var p = new ViewerProject
        {
            Title = new DirectoryInfo(dir).Name + " / " + difficulty
        };
        p.Chart = ext is ".vsb" or ".vsc" ? path : Existing(dir, difficulty + ".vsb", difficulty + ".vsc");
        p.Gimmick = ext == ".vsm" ? path : Existing(dir, difficulty + ".vsm", "GLOBAL.vsm");
        p.Images = ext == ".vsp" ? path : null;
        p.Audio = Existing(dir, "music.ogg", "song.ogg", "song.wav", "music.wav", "song.mp3", "music.mp3", "song.flac", "music.flac");
        // 只有在命名类别下存在唯一候选时才接受导出的游戏音乐。
        // 绝不能因为某个 .ogg 恰好排在第一个，就把人声/前奏文件当成歌曲音频。
        p.Audio ??= FindUniqueChartAudio(dir);
        p.Notes = true;
        // 即使 VSM 还没有对应的音符，轨道本身也应当可以查看。
        string? info = Existing(dir, "info.json", "song.json");
        if (info != null)
        {
            try
            {
                using var json = JsonDocument.Parse(File.ReadAllText(info));
                var j = json.RootElement;
                if (j.TryGetProperty("bpm_display", out var b) && double.TryParse(b.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture,
                    out double bpm) && bpm > 0 && bpm <= 10000)
                {
                    p.Bpm = bpm;
                }
                if (j.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String)
                {
                    p.Title = n.GetString() + " / " + difficulty;
                }
            }
            catch (JsonException)
            {
                /* 可选的歌曲标题损坏不应遮住谱面。 */
            }
        }
        return p;
    }

    /// <summary>取唯一的 music_chart_* 音频；一旦存在两个及以上候选就返回 null，宁可不挂音频也不猜。</summary>
    static string? FindUniqueChartAudio(string directory)
    {
        // Take(2) 只为判断"是否唯一"，不需要枚举全部候选。
        var matches = Directory.EnumerateFiles(directory).Where(file => Path.GetFileNameWithoutExtension(file).StartsWith("music_chart_",
            StringComparison.OrdinalIgnoreCase)
            && Path.GetExtension(file).ToLowerInvariant() is ".ogg" or ".wav" or ".mp3" or ".flac").Order(StringComparer.Ordinal).Take(2).ToArray();
        return matches.Length == 1 ? matches[0] : null;
    }

    /// <summary>读取难度专属或共用的 _cgmk_config.json 布尔开关；文件缺失或该键不是布尔值时返回 <paramref name="fallback"/>，不把缺省当作 false。</summary>
    public static bool Config(ViewerProject p, string name, bool fallback = true)
    {
        string? root = Root(p);
        if (root == null)
        {
            return fallback;
        }
        string? path = Existing(root, Difficulty(p.Chart ?? p.Gimmick ?? p.Images!) + "_cgmk_config.json", "cgmk_config.json");
        if (path == null)
        {
            return fallback;
        }
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        return doc.RootElement.TryGetProperty(name, out var value)
            && value.ValueKind is JsonValueKind.True or JsonValueKind.False? value.GetBoolean() : fallback;
    }

    /// <summary>同上，取字符串项；难度专属配置优先于目录共用的 cgmk_config.json。</summary>
    public static string ConfigString(ViewerProject p, string name, string fallback)
    {
        string? root = Root(p);
        if (root == null)
        {
            return fallback;
        }
        string? path = Existing(root, Difficulty(p.Chart ?? p.Gimmick ?? p.Images ?? p.Jacket!) + "_cgmk_config.json", "cgmk_config.json");
        if (path == null)
        {
            return fallback;
        }
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        return doc.RootElement.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String? value.GetString() ! : fallback;
    }
}

