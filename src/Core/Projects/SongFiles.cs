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

    /// <summary>
    /// 把谱面/VSM/VSP 路径、歌曲信息文件或歌曲目录展开成工程；目录形式按固定难度候选顺序挑选，不依赖文件系统枚举顺序。
    /// 直接给出 info.json / song.json 等同于打开它所在的歌曲目录；shatterinfo.json 则直接落到 SHATTER 谱面——
    /// 它只描述这一个难度，按目录优先级打开会落到同目录的常规难度上去。
    /// </summary>
    public static ViewerProject Open(string path)
    {
        path = Path.GetFullPath(path);
        if (File.Exists(path) && SongInfo.IsInfoFile(path))
        {
            string folder = Path.GetDirectoryName(path)!;
            path = (Path.GetFileName(path).Equals(SongInfo.ShatterFile, StringComparison.OrdinalIgnoreCase)
                ? Existing(folder, "SHATTER.vsb", "SHATTER.vsc", "SHATTER.vsm") : null) ?? folder;
        }
        if (Directory.Exists(path))
        {
            path = PreferredChart(path) ?? throw new FileNotFoundException("This folder contains no .vsb, .vsc or .vsm.");
        }
        string ext = Path.GetExtension(path).ToLowerInvariant();
        if (ext is not (".vsb" or ".vsc" or ".vsm" or ".vsp"))
        {
            throw new InvalidDataException("Open a .vsb, .vsc, .vsm, .vsp, info.json, shatterinfo.json, song folder or .sgv.json project.");
        }
        string dir = Path.GetDirectoryName(path)!, difficulty = Difficulty(path);
        var p = new ViewerProject
        {
            Title = new DirectoryInfo(dir).Name + " / " + difficulty
        };
        p.Chart = ext is ".vsb" or ".vsc" ? path : Existing(dir, difficulty + ".vsb", difficulty + ".vsc");
        p.Gimmick = ext == ".vsm" ? path : Existing(dir, difficulty + ".vsm", "GLOBAL.vsm");
        p.Images = ext == ".vsp" ? path : null;
        p.Notes = true;
        // 即使 VSM 还没有对应的音符，轨道本身也应当可以查看。
        // BACKSTAGE 的音频与封面由 enc_data 明确指定，必须先于同目录候选顺序生效：
        // crimson 这类谱包里 music.ogg / jacket.png 属于主曲，ENCORE 用的是 music_chart_scarlet.ogg / song_scarlet_0.png。
        var info = SongInfo.Read(dir);
        var view = info?.Effective(difficulty);
        if (view != null)
        {
            p.Audio = view.AudioId == null ? null : Existing(dir, view.AudioId);
            p.Jacket = view.Jacket == null ? null : Existing(dir, view.Jacket);
            if (double.TryParse(view.BpmDisplay, NumberStyles.Float, CultureInfo.InvariantCulture, out double bpm)
                && bpm > 0 && bpm <= 10000)
            {
                p.Bpm = bpm;
            }
            if (view.Name is { Length: > 0 } name)
            {
                p.Title = name + " / " + view.DisplayDifficulty;
            }
        }
        p.Audio ??= Existing(dir, "music.ogg", "song.ogg", "song.wav", "music.wav", "song.mp3", "music.mp3", "song.flac", "music.flac");
        // 只有在命名类别下存在唯一候选时才接受导出的游戏音乐。
        // 绝不能因为某个 .ogg 恰好排在第一个，就把人声/前奏文件当成歌曲音频。
        p.Audio ??= FindUniqueChartAudio(dir);
        return p;
    }

    /// <summary>
    /// 打开歌曲目录时选中的那张谱面。固定的难度优先级，保证同一目录每次打开都落到同一个谱面，
    /// 不依赖文件系统枚举顺序；SHATTER 排在四个常规难度之后：同目录有常规谱面时它不该抢先，
    /// 只有它一张谱面时也不必落进按字母枚举的兜底。目录里一张谱面都没有时返回 null。
    /// 最近打开列表要显示的难度也走这里，否则列表上写的和点进去看到的可能不是同一张谱。
    /// </summary>
    public static string? PreferredChart(string directory)
    {
        foreach (string candidate in new[]
        {
            "ENCORE",
            "FINALE",
            "MIDDLE",
            "OPENING",
            "SHATTER",
            "GLOBAL"
        })
        {
            if (Existing(directory, candidate + ".vsb", candidate + ".vsc", candidate + ".vsm") is { } chosen)
            {
                return chosen;
            }
        }
        return Directory.EnumerateFiles(directory)
            .Where(p => Path.GetExtension(p).ToLowerInvariant() is ".vsb" or ".vsc" or ".vsm").Order().FirstOrDefault();
    }

    /// <summary>
    /// 一个可切换的难度。<c>Chart</c> 为 null 表示该难度在这个目录下没有谱面文件（谱包可能只写了信息槽
    /// 或 shatterinfo.json 却没放谱面），此时只作为不可点的占位显示。
    /// </summary>
    public sealed record SongDifficulty(string Name, string Display, string? Level, string? Designer, string? Chart, bool Backstage);

    /// <summary>
    /// 枚举目录下的难度，顺序固定为 OPENING→SHATTER，不依赖文件系统枚举顺序。
    /// info.json 缺失时仍按文件名列出，只是没有等级与谱师文字。
    /// </summary>
    public static SongDifficulty[] Difficulties(string directory, SongInfo? info)
    {
        var result = new List<SongDifficulty>();
        foreach (string name in SongInfo.Order)
        {
            string? chart = Existing(directory, name + ".vsb", name + ".vsc");
            var view = info?.Effective(name);
            // 没有谱面又没有信息槽的难度完全不存在，不作为占位显示。
            if (chart == null && (view == null || (view.Level == null && view.Constant == null)))
            {
                continue;
            }
            result.Add(new(name, view?.DisplayDifficulty ?? name, view == null ? null : SongInfo.Level(view),
                view?.Designer, chart, view?.Backstage ?? false));
        }
        return [.. result];
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

