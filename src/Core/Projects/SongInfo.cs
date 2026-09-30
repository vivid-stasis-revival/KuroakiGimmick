using System.Globalization;
using System.Text.Json;

namespace KuroakiGimmick.Core;

/// <summary>
/// info.json / song.json 的歌曲信息。ENCORE 的曲名、曲师、BPM、封面与音频由 enc_data 覆盖，
/// enc_data 缺项回落主曲（部分谱包只在 enc_data 里写 audio_id / jacket）；难度等级与谱师取主曲的难度槽，
/// SHATTER 的以同目录 shatterinfo.json 为准。
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
    public string? AudioId { get; private init; }
    public string? Jacket { get; private init; }
    public string? PreviewId { get; private init; }
    public string? Version { get; private init; }
    public bool HasEncore { get; private init; }
    public bool JacketAnimated { get; private init; }
    /// <summary>enc_data 的原始内容；缺失时为 null，此时任何难度都不套用 backstage 语义。</summary>
    public EncoreData? Encore { get; private init; }
    Dictionary<int, SlotData> slots = [];
    /// <summary>shatterinfo.json 的内容；目录里没有这份文件时为 null。</summary>
    public ShatterData? Shatter { get; private set; }

    /// <summary>主曲第 N 槽的等级与谱师。SHATTER 这类没有对应槽的难度读不到值，显示为空而不是借用别的槽。</summary>
    public sealed record SlotData(double? Constant, string? Display, string? Designer);

    /// <summary>
    /// SHATTER 自己的信息文件。歌曲信息只有 info.json 与它两种，其余难度都写在 info.json 的 _N 槽位里、
    /// 没有单独的文件。它的字段是单数的 difficulty_number / note_designer，所以主文件的第 5 槽几乎总是空的，
    /// 等级与谱师只能从这里取；这类谱包多半连 info.json 都没有，曲名、曲师那些全曲字段也一并写在这里。
    /// </summary>
    public sealed record ShatterData(string Path, string? DifficultyName, string? ChartId, string? Name,
        string? FormattedName, string? Artist, string? BpmDisplay, string? JacketArtist, string? AudioId,
        string? Jacket, string? PreviewId, string? Version, string? Level, string? Designer, bool JacketAnimated);

    public const string ShatterFile = "shatterinfo.json";

    /// <summary>
    /// 能当作歌曲目录打开的信息文件。命令行、拖放与打开对话框共用这一个判断，免得三处各认各的，
    /// 漏掉的那处表现出来就是"这个文件打不开"。
    /// </summary>
    public static bool IsInfoFile(string path) =>
        System.IO.Path.GetFileName(path).ToLowerInvariant() is "info.json" or "song.json" or ShatterFile;

    /// <summary>
    /// enc_data。<c>HideBackstage</c> 为真才不算 backstage，缺省视为 backstage —— 与原版
    /// song_get_info 的 BACKSTAGE 标签规则一致，谱面文件叫 ENCORE 而 UI 叫 BACKSTAGE 是正常的。
    /// </summary>
    public sealed record EncoreData(int? SongId, string? Name, string? FormattedName, string? AudioId,
        string? Jacket, string? PreviewId, string? BpmDisplay, string? Artist, string? JacketArtist, bool HideBackstage, bool JacketAnimated);

    /// <summary>
    /// 某个难度实际生效的显示信息。BACKSTAGE 的资源按 enc_data → 主曲回落；
    /// SHATTER 的资源只认 shatterinfo，自身缺项时保持 null，让调用方按 CSM 语义回落到 music.ogg / jacket.*，
    /// 绝不能借用同目录 info.json 显式指定的主曲资源。
    /// </summary>
    public sealed record SongView(string Difficulty, string DisplayDifficulty, bool Backstage, bool UsesEncore,
        string? Name, string? FormattedName, string? Artist, string? BpmDisplay, string? JacketArtist,
        string? AudioId, string? Jacket, string? PreviewId, double? Constant, string? Level, string? Designer, bool JacketAnimated);

    /// <summary>
    /// 读取目录下的 info.json / song.json 与 shatterinfo.json。两份都没有才返回 null，解析失败经
    /// <paramref name="error"/> 上报；info.json 缺了但 shatterinfo.json 还在时，全曲字段由后者顶上——
    /// Shatter 谱包大多只发这一份文件，整首歌的信息都在里面。
    /// </summary>
    public static SongInfo? Read(string directory, Action<string>? error = null)
    {
        var shatter = SongFiles.Existing(directory, ShatterFile) is { } side ? ReadShatter(side, error) : null;
        var info = SongFiles.Existing(directory, "info.json", "song.json") is { } main ? ReadMain(main, error) : null;
        if (info == null)
        {
            if (shatter == null)
            {
                return null;
            }
            // info.json 不在（或者刚刚已经报过解析失败）时拿 shatterinfo.json 当全曲信息。
            info = new SongInfo
            {
                Path = shatter.Path,
                ChartId = shatter.ChartId,
                Name = shatter.Name,
                FormattedName = shatter.FormattedName,
                Artist = shatter.Artist,
                BpmDisplay = shatter.BpmDisplay,
                JacketArtist = shatter.JacketArtist,
                AudioId = shatter.AudioId,
                Jacket = shatter.Jacket,
                PreviewId = shatter.PreviewId,
                Version = shatter.Version,
                JacketAnimated = shatter.JacketAnimated
            };
        }
        info.Shatter = shatter;
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
            AudioId = Text(j, "audio_id"),
            Jacket = Text(j, "jacket"),
            PreviewId = Text(j, "preview_id"),
            Version = Text(j, "version"),
            HasEncore = j.TryGetProperty("has_encore", out var has) && has.ValueKind == JsonValueKind.True,
            JacketAnimated = Bool(j, "jacket_animated"),
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

    static ShatterData? ReadShatter(string file, Action<string>? error) => Load(file, error, j =>
        new ShatterData(file, Text(j, "difficulty_name"), Text(j, "chart_id"), Text(j, "name"),
            Text(j, "formatted_name"), Text(j, "artist"), Text(j, "bpm_display"), Text(j, "jacket_artist"),
            Text(j, "audio_id"), Text(j, "jacket"), Text(j, "preview_id"), Text(j, "version"),
            Text(j, "difficulty_number"), Text(j, "note_designer"), Bool(j, "jacket_animated")));

    /// <summary>info.json 与 shatterinfo.json 共用的读取壳：4 MiB 上限、根必须是对象、解析失败只上报不抛错。</summary>
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
            e.TryGetProperty("hide_backstage", out var hide) && hide.ValueKind == JsonValueKind.True, Bool(e, "jacket_animated"));
    }

    /// <summary>字符串或数字都接受，空串视为缺失；其余类型（数组、对象、null）一律当作没写。</summary>
    static string? Text(JsonElement parent, string key) => parent.TryGetProperty(key, out var v)
        && v.ValueKind is JsonValueKind.String or JsonValueKind.Number && v.ToString() is { Length: > 0 } s ? s : null;

    static double? Number(JsonElement parent, string key) => parent.TryGetProperty(key, out var v)
        && double.TryParse(v.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out double d) ? d : null;

    static bool Bool(JsonElement parent, string key) => parent.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.True;

    /// <summary>该难度是否走 enc_data。谱面文件叫 ENCORE 或 BACKSTAGE 都算，且 enc_data 必须存在。</summary>
    public bool UsesEncore(string difficulty) => Encore != null && Normalize(difficulty) is "ENCORE" or "BACKSTAGE";

    /// <summary>
    /// 该难度在 UI 上的名字。走 enc_data 且没有 hide_backstage 时 ENCORE 显示为 BACKSTAGE；
    /// shatterinfo.json 写了 difficulty_name 就照写的显示，自造难度名（有谱包把 SHATTER 叫成 EVIL）才出得来。
    /// </summary>
    public string DisplayDifficulty(string difficulty) => UsesEncore(difficulty) && !Encore!.HideBackstage
        ? "BACKSTAGE" : ShatterOf(difficulty)?.DifficultyName ?? Normalize(difficulty);

    public SlotData? SlotOf(string difficulty) => slots.GetValueOrDefault(Slot(difficulty));

    /// <summary>
    /// 只有 SHATTER 难度才读 shatterinfo.json。认的是谱面文件名而不是文件里的 difficulty_name：
    /// 有谱包在里面写着 EVIL，那条谱面仍旧是 SHATTER.vsc，只有界面上的难度名跟着改。
    /// </summary>
    ShatterData? ShatterOf(string difficulty) => Normalize(difficulty) == "SHATTER" ? Shatter : null;

    /// <summary>
    /// 合并出该难度实际生效的显示信息。enc_data 逐字段覆盖主曲，缺项回落 —— terabyte 这类只写了
    /// audio_id / jacket 的谱包必须保留主曲的曲名与 BPM，不能因为存在 enc_data 就整体替换。
    /// 全曲字段里 shatterinfo.json 排在 info.json 之后：它写的常是"曲名 [Shatter]"这种带难度后缀的变体，
    /// info.json 写了就该听 info.json 的。等级与谱师反过来以 shatterinfo.json 为准，那才是这个难度自己的数据。
    /// </summary>
    public SongView Effective(string difficulty)
    {
        bool encore = UsesEncore(difficulty);
        var e = encore ? Encore : null;
        var slot = SlotOf(difficulty);
        var d = ShatterOf(difficulty);
        // CSM 的 SHATTER 是独立 song entry：缺 audio_id / jacket / preview_id 时回落目录默认资源，
        // 不继承 info.json 的显式资源。普通难度才读取主曲字段；BACKSTAGE 则按 enc_data → 主曲回落。
        string? audio = encore ? e?.AudioId ?? AudioId : d != null ? d.AudioId : AudioId;
        string? jacket = encore ? e?.Jacket ?? Jacket : d != null ? d.Jacket : Jacket;
        string? preview = encore ? e?.PreviewId ?? e?.AudioId ?? AudioId : d != null ? d.PreviewId : PreviewId;
        // CSM 的 BACKSTAGE 没写独立 jacket 时直接复用主曲已经加载好的 sprite，所以也继承主曲的 animated 状态；
        // 只有 enc_data 明确换了 jacket 才使用它自己的 jacket_animated。SHATTER 则由 shatterinfo 自己的标记决定。
        bool animated = encore
            ? (e?.Jacket != null ? e.JacketAnimated : JacketAnimated)
            : d != null ? d.JacketAnimated : JacketAnimated;
        return new(Normalize(difficulty), DisplayDifficulty(difficulty), encore && !Encore!.HideBackstage, encore,
            e?.Name ?? Name ?? d?.Name, e?.FormattedName ?? FormattedName ?? d?.FormattedName,
            e?.Artist ?? Artist ?? d?.Artist, e?.BpmDisplay ?? BpmDisplay ?? d?.BpmDisplay,
            e?.JacketArtist ?? JacketArtist ?? d?.JacketArtist, audio, jacket, preview,
            slot?.Constant, d?.Level ?? slot?.Display, d?.Designer ?? slot?.Designer, animated);
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
