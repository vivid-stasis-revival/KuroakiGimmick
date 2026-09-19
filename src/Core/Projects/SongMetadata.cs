using System.Text.Json;

namespace KuroakiGimmick.Core;

/// <summary>
/// 读取供 HUD 显示的歌曲元数据。显示文字不参与对象加载器选择。
/// </summary>
public sealed record SongMetadata(string Name, string Artist, string Difficulty, string Level, int DifficultyFrame)
{
    /// <summary>
    /// 读取 info.json/song.json 的显示文字；命令行给出的 --song-* 覆盖文件内容。
    /// 元数据缺失或损坏只记诊断、不抛错，HUD 退回谱面标题与 "?" 等级。
    /// </summary>
    public static SongMetadata Load(ViewerProject p, Chart chart)
    {
        string basis = p.Chart ?? p.Gimmick ?? p.Images ?? "", difficulty = SongFiles.Difficulty(basis).ToUpperInvariant().Replace("_STORY", "");
        // slot 是原版难度编号，用于挑选 difficulty_display_N；未知难度归 0。
        int slot = difficulty switch
        {
            "OPENING" => 1,
            "MIDDLE" => 2,
            "FINALE" => 3,
            "ENCORE" => 4,
            "SHATTER" => 5,
            _ => 0
        };
        string name = chart.Title, artist = "", level = "?";
        bool backstage = false;
        string? root = SongFiles.Root(p), file = root == null ? null : SongFiles.Existing(root, "info.json", "song.json");
        if (file != null)
        {
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(file));
                var j = doc.RootElement;
                string S(JsonElement e, string key, string fallback) => e.TryGetProperty(key, out var v)
                    && v.ValueKind is JsonValueKind.String or JsonValueKind.Number? v.ToString() : fallback;
                name = S(j, "name", S(j, "formatted_name", name));
                artist = S(j, "artist", artist);
                level = S(j, "difficulty_display_" + slot, level);
                if (slot == 4 && j.TryGetProperty("enc_data", out var enc) && enc.ValueKind == JsonValueKind.Object)
                {
                    // ENCORE 的 enc_data 覆盖曲名与曲师；hide_backstage 为 true 才不算 backstage，缺省视为 backstage。
                    backstage = !(enc.TryGetProperty("hide_backstage", out var hide) && hide.ValueKind == JsonValueKind.True);
                    name = S(enc, "name", S(enc, "formatted_name", name));
                    artist = S(enc, "artist", artist);
                }
            }
            catch (Exception ex) when (ex is IOException or JsonException or InvalidOperationException)
            {
                chart.Diagnostics.Add(new("song-info", 0, "Song UI metadata unavailable: " + ex.Message));
            }
        }
        // DifficultyFrame 是 HUD 难度图框的索引：backstage 用第 5 张，已知难度 1..4 映射到 0..3，其余归到 4。
        return new(p.SongName ?? name, p.SongArtist ?? artist, difficulty, p.SongLevel ?? level,
            backstage ? 5 : slot is >= 1 and <= 4 ? slot - 1 : 4);
    }
}
