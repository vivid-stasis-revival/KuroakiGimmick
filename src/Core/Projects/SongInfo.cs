using System.Globalization;
using System.Text.Json;

namespace KuroakiGimmick.Core;

/// <summary>
/// info.json / song.json 的歌曲信息。ENCORE 的曲名、曲师、BPM、封面与音频由 enc_data 覆盖，
/// enc_data 缺项回落主曲（部分谱包只在 enc_data 里写 audio_id / jacket）；难度等级与谱师取主曲的难度槽，
/// 该难度另有附属文件（shatterinfo.json 这类）时以附属文件为准。
/// 解析失败只上报错误文本、不抛错，损坏的显示信息不应遮住谱面本身。
/// </summary>
public sealed class SongInfo
{
    /// <summary>原版难度编号，用于挑选 difficulty_display_N；BACKSTAGE 与 ENCORE 共用第 4 槽，未知难度归 0。</summary>
    public static int Slot(string difficulty) => Normalize(difficulty) switch
    {
        "OPENING" => 1,
        "MIDDLE" => 2,
        "FINALE" => 3,
        "ENCORE" or "BACKSTAGE" => 4,
        "SHATTER" => 5,
        _ => 0
    };

    /// <summary>难度标识统一成大写并去掉 _STORY 后缀，文件名与 UI 名称的差异只在 <see cref="Effective"/> 里体现。</summary>
    public static string Normalize(string difficulty) => difficulty.ToUpperInvariant().Replace("_STORY", "");

    /// <summary>固定的难度顺序，同时也是目录打开时的候选顺序来源；不按字母排序。</summary>
    public static readonly string[] Order = ["OPENING", "MIDDLE", "FINALE", "ENCORE", "SHATTER"];

    public string Path { get; private init; } = "";
    public string? ChartId { get; private init; }
    public string? Name { get; private init; }
    public string? FormattedName { get; private init; }
    public string? Artist { get; private init; }
    public string? BpmDisplay { get; private init; }
    public string? JacketArtist { get; private init; }
    public string? Version { get; private init; }
    public bool HasEncore { get; private init; }
    /// <summary>enc_data 的原始内容；缺失时为 null，此时任何难度都不套用 backstage 语义。</summary>
    public EncoreData? Encore { get; private init; }
    Dictionary<int, SlotData> slots = [];
    Dictionary<int, DifficultyFile> perDifficulty = [];

    /// <summary>主曲第 N 槽的等级与谱师。SHATTER 这类没有对应槽的难度读不到值，显示为空而不是借用别的槽。</summary>
    public sealed record SlotData(double? Constant, string? Display, string? Designer);

    /// <summary>
    /// 难度附属文件 <c>&lt;difficulty&gt;info.json</c>。现实里只有 SHATTER 会单独发一份：字段是单数的
    /// difficulty_number / note_designer 而不是带 _N 后缀的槽位字段，所以主文件的第 5 槽几乎总是空的，
    /// 等级与谱师只能从这里取。这类谱包多半连主文件都没有，曲名、曲师那些全曲字段也一并写在这里。
    /// </summary>
    public sealed record DifficultyFile(int Slot, string Path, string? DifficultyName, string? ChartId, string? Name,
        string? FormattedName, string? Artist, string? BpmDisplay, string? JacketArtist, string? Version,
        string? Level, string? Designer);

    /// <summary>
    /// enc_data。<c>HideBackstage</c> 为真才不算 backstage，缺省视为 backstage —— 与原版
    /// song_get_info 的 BACKSTAGE 标签规则一致，谱面文件叫 ENCORE 而 UI 叫 BACKSTAGE 是正常的。
    /// </summary>
    public sealed record EncoreData(int? SongId, string? Name, string? FormattedName, string? AudioId,
        string? Jacket, string? PreviewId, string? BpmDisplay, string? Artist, string? JacketArtist, bool HideBackstage);

    /// <summary>
    /// 某个难度实际生效的显示信息。<c>AudioId</c> 与 <c>Jacket</c> 只在 info.json 明确写出时才有值；
    /// 没写就保持 null，由调用方沿用原有的同目录候选顺序，绝不在这里猜文件名。
    /// </summary>
    public sealed record SongView(string Difficulty, string DisplayDifficulty, bool Backstage, bool UsesEncore,
        string? Name, string? FormattedName, string? Artist, string? BpmDisplay, string? JacketArtist,
        string? AudioId, string? Jacket, string? PreviewId, double? Constant, string? Level, string? Designer);

    /// <summary>
    /// 读取目录下的 info.json / song.json，连同各难度自带的 <c>&lt;difficulty&gt;info.json</c>。
    /// 两类文件一个都没有才返回 null，解析失败经 <paramref name="error"/> 上报；主文件缺了但附属文件还在时，
    /// 全曲字段由附属文件顶上——Shatter 谱包大多只发一份 shatterinfo.json，整首歌的信息都在里面。
    /// </summary>
    public static SongInfo? Read(string directory, Action<string>? error = null)
    {
        Dictionary<int, DifficultyFile> perDifficulty = [];
        string[] sidecars = [.. Order.Select(d => d.ToLowerInvariant() + "info.json")];
        // 先一次性问"有没有任何一份附属文件"。绝大多数谱包一份都没有，逐个难度去探等于把同一个目录枚举五遍。
        if (SongFiles.Existing(directory, sidecars) != null)
        {
            foreach (string difficulty in Order)
            {
                // 槽位认文件名不认文件内容：游戏就是按难度拼出这个文件名去找的。difficulty_name 只是显示用的标签，
                // 真有谱包在 shatterinfo.json 里写着 EVIL，那条谱面仍旧是 SHATTER.vsc，仍旧算第 5 槽。
                if (SongFiles.Existing(directory, difficulty.ToLowerInvariant() + "info.json") is not { } side)
                {
                    continue;
                }
                if (ReadDifficulty(side, Slot(difficulty), error) is { } data)
                {
                    perDifficulty[data.Slot] = data;
                }
            }
        }
        var info = SongFiles.Existing(directory, "info.json", "song.json") is { } main ? ReadMain(main, error) : null;
        if (info == null)
        {
            if (perDifficulty.Count == 0)
            {
                return null;
            }
            // 主文件不在（或者刚刚已经报过解析失败）时按固定难度顺序取第一个附属文件当全曲信息。
            var first = perDifficulty.OrderBy(kv => kv.Key).First().Value;
            info = new SongInfo
            {
                Path = first.Path,
                ChartId = first.ChartId,
                Name = first.Name,
                FormattedName = first.FormattedName,
                Artist = first.Artist,
                BpmDisplay = first.BpmDisplay,
                JacketArtist = first.JacketArtist,
                Version = first.Version
            };
        }
        info.perDifficulty = perDifficulty;
        return info;
    }

    static SongInfo? ReadMain(string file, Action<string>? error) => Load(file, error, j =>
    {
        var info = new SongInfo
        {
            Path = file,
            ChartId = Text(j, "chart_id"),
            Name = Text(j, "name"),
            FormattedName = Text(j, "formatted_name"),
            Artist = Text(j, "artist"),
            BpmDisplay = Text(j, "bpm_display"),
            JacketArtist = Text(j, "jacket_artist"),
            Version = Text(j, "version"),
            HasEncore = j.TryGetProperty("has_encore", out var has) && has.ValueKind == JsonValueKind.True,
            Encore = ReadEncore(j)
        };
        foreach (int slot in new[] { 1, 2, 3, 4, 5 })
        {
            var data = new SlotData(Number(j, "difficulty_constant_" + slot), Text(j, "difficulty_display_" + slot),
                Text(j, "note_designer_" + slot));
            if (data != new SlotData(null, null, null))
            {
                info.slots[slot] = data;
            }
        }
        return info;
    });

    static DifficultyFile? ReadDifficulty(string file, int slot, Action<string>? error) => Load(file, error, j =>
        new DifficultyFile(slot, file, Text(j, "difficulty_name"), Text(j, "chart_id"), Text(j, "name"),
            Text(j, "formatted_name"), Text(j, "artist"), Text(j, "bpm_display"), Text(j, "jacket_artist"),
            Text(j, "version"), Text(j, "difficulty_number"), Text(j, "note_designer")));

    /// <summary>主文件与难度附属文件共用的读取壳：4 MiB 上限、根必须是对象、解析失败只上报不抛错。</summary>
    static T? Load<T>(string file, Action<string>? error, Func<JsonElement, T?> build) where T : class
    {
        try
        {
            if (new FileInfo(file).Length > 4 * 1024 * 1024)
            {
                throw new InvalidDataException("Song info exceeds the 4 MiB limit.");
            }
            using var doc = JsonDocument.Parse(File.ReadAllText(file));
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidDataException("Song info root is not an object.");
            }
            return build(doc.RootElement);
        }
        catch (Exception ex) when (ex is IOException or JsonException or InvalidDataException or InvalidOperationException)
        {
            error?.Invoke("Song info unavailable: " + ex.Message);
            return null;
        }
    }

    static EncoreData? ReadEncore(JsonElement root)
    {
        if (!root.TryGetProperty("enc_data", out var e) || e.ValueKind != JsonValueKind.Object)
        {
            return null;
        }
        return new((int?)Number(e, "song_id"), Text(e, "name"), Text(e, "formatted_name"), Text(e, "audio_id"),
            Text(e, "jacket"), Text(e, "preview_id"), Text(e, "bpm_display"), Text(e, "artist"), Text(e, "jacket_artist"),
            e.TryGetProperty("hide_backstage", out var hide) && hide.ValueKind == JsonValueKind.True);
    }

    /// <summary>字符串或数字都接受，空串视为缺失；其余类型（数组、对象、null）一律当作没写。</summary>
    static string? Text(JsonElement parent, string key) => parent.TryGetProperty(key, out var v)
        && v.ValueKind is JsonValueKind.String or JsonValueKind.Number && v.ToString() is { Length: > 0 } s ? s : null;

    static double? Number(JsonElement parent, string key) => parent.TryGetProperty(key, out var v)
        && double.TryParse(v.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out double d) ? d : null;

    /// <summary>该难度是否走 enc_data。谱面文件叫 ENCORE 或 BACKSTAGE 都算，且 enc_data 必须存在。</summary>
    public bool UsesEncore(string difficulty) => Encore != null && Normalize(difficulty) is "ENCORE" or "BACKSTAGE";

    /// <summary>
    /// 该难度在 UI 上的名字。走 enc_data 且没有 hide_backstage 时 ENCORE 显示为 BACKSTAGE；
    /// 附属文件写了 difficulty_name 就照写的显示，自造难度名（有谱包把 SHATTER 叫成 EVIL）才出得来。
    /// </summary>
    public string DisplayDifficulty(string difficulty) => UsesEncore(difficulty) && !Encore!.HideBackstage
        ? "BACKSTAGE" : FileOf(difficulty)?.DifficultyName ?? Normalize(difficulty);

    public SlotData? SlotOf(string difficulty) => slots.GetValueOrDefault(Slot(difficulty));

    /// <summary>该难度的附属文件；没有就是 null，此时等级与谱师照旧只看主曲的难度槽。</summary>
    public DifficultyFile? FileOf(string difficulty) => perDifficulty.GetValueOrDefault(Slot(difficulty));

    /// <summary>
    /// 合并出该难度实际生效的显示信息。enc_data 逐字段覆盖主曲，缺项回落 —— terabyte 这类只写了
    /// audio_id / jacket 的谱包必须保留主曲的曲名与 BPM，不能因为存在 enc_data 就整体替换。
    /// 全曲字段里附属文件排在主曲之后：它写的常是"曲名 [Shatter]"这种带难度后缀的变体，主曲写了就该听主曲的。
    /// 等级与谱师反过来以附属文件为准，那才是这个难度自己的数据。
    /// </summary>
    public SongView Effective(string difficulty)
    {
        bool encore = UsesEncore(difficulty);
        var e = encore ? Encore : null;
        var slot = SlotOf(difficulty);
        var d = FileOf(difficulty);
        return new(Normalize(difficulty), DisplayDifficulty(difficulty), encore && !Encore!.HideBackstage, encore,
            e?.Name ?? Name ?? d?.Name, e?.FormattedName ?? FormattedName ?? d?.FormattedName,
            e?.Artist ?? Artist ?? d?.Artist, e?.BpmDisplay ?? BpmDisplay ?? d?.BpmDisplay,
            e?.JacketArtist ?? JacketArtist ?? d?.JacketArtist, e?.AudioId, e?.Jacket, e?.PreviewId,
            slot?.Constant, d?.Level ?? slot?.Display, d?.Designer ?? slot?.Designer);
    }

    /// <summary>
    /// 没有 difficulty_display_N 时按 difficulty_constant_N 生成等级文字，对应原版 load_song_information
    /// 的兜底：小数第一位 ≥ 5 显示 "N+"，否则显示 "N"。
    /// </summary>
    public static string? Level(SongView view)
    {
        if (view.Level is { Length: > 0 } display)
        {
            // ConfigParser / JSON 里偶尔写成 15.0，游戏显示的是 15。
            return double.TryParse(display, NumberStyles.Float, CultureInfo.InvariantCulture, out double whole)
                && whole == Math.Floor(whole) && display.Contains('.')
                ? ((int)whole).ToString(CultureInfo.InvariantCulture) : display;
        }
        if (view.Constant is not { } c || !double.IsFinite(c) || c < 0)
        {
            return null;
        }
        int level = (int)Math.Floor(c);
        return (int)Math.Floor(c * 10 + .5) % 10 >= 5
            ? level.ToString(CultureInfo.InvariantCulture) + "+"
            : level.ToString(CultureInfo.InvariantCulture);
    }
}
