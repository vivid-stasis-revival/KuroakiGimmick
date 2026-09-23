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
        string basis = p.Chart ?? p.Gimmick ?? p.Images ?? "", difficulty = SongInfo.Normalize(SongFiles.Difficulty(basis));
        int slot = SongInfo.Slot(difficulty);
        string name = chart.Title, artist = "", level = "?";
        bool backstage = false;
        string? root = SongFiles.Root(p);
        var info = root == null ? null : SongInfo.Read(root,
            m => chart.Diagnostics.Add(new("song-info", 0, "Song UI metadata unavailable: " + m)));
        if (info != null)
        {
            // ENCORE 的曲名、曲师由 enc_data 覆盖；等级与谱师仍取主曲的第 4 槽。
            var view = info.Effective(difficulty);
            name = view.Name ?? view.FormattedName ?? name;
            artist = view.Artist ?? artist;
            level = SongInfo.Level(view) ?? level;
            backstage = view.Backstage;
        }
        // DifficultyFrame 是 HUD 难度图框的索引：backstage 用第 5 张，已知难度 1..4 映射到 0..3，其余归到 4。
        return new(p.SongName ?? name, p.SongArtist ?? artist, difficulty, p.SongLevel ?? level,
            backstage ? 5 : slot is >= 1 and <= 4 ? slot - 1 : 4);
    }
}
