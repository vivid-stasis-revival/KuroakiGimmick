namespace KuroakiGimmick.Core;

/// <summary>
/// 最近打开列表一行的标题。谱面文件名就是难度（ENCORE.vsc、BACKSTAGE.vsb），直接把文件名当标题，
/// 列表里看到的是一排难度名而不是歌；这里按歌曲目录的 info.json / shatterinfo.json 还原成
/// "曲名 / 曲师 @ 难度  LV.等级"，与左栏难度条取自同一套歌曲信息。
/// 只负责算文字，不缓存：读盘由调用方按自己的绘制节奏决定，Draw 里不该每帧读 JSON。
/// </summary>
public static class RecentSource
{
    /// <summary>
    /// 最近列表的一行：<c>Path</c> 决定点什么、能不能点，<c>Title</c> 只决定怎么显示。
    /// 欢迎页与原生菜单共用它，两处入口不会各显示各的。
    /// </summary>
    public sealed record Item(string Path, string Title);

    /// <summary>
    /// 一行标题。歌曲信息读不到（没有信息文件、只有一张散落的谱面、路径已经不存在）时退回原来的
    /// 文件名写法，不因为缺一份 info.json 就让整行消失；读取失败同样只退回文件名。
    /// </summary>
    public static string Title(string path)
    {
        string fallback = FileName(path);
        try
        {
            if (!TrySong(path, out string directory, out string difficulty))
            {
                return fallback;
            }
            var view = SongInfo.Read(directory)?.Effective(difficulty);
            if (view == null)
            {
                return fallback;
            }
            // 信息文件里连曲名都没有（有谱包只写难度槽）时用目录名——文件名在这里等于难度名，拿它当曲名
            // 会写出 "ENCORE @ BACKSTAGE" 这种读不通的标题。
            string name = view.Name ?? view.FormattedName ?? new DirectoryInfo(directory).Name;
            return Compose(name, view.Artist, view.DisplayDifficulty, SongInfo.Level(view));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return fallback;
        }
    }

    /// <summary>
    /// 谱面与歌曲目录能还原成一首歌；.sgv.json 工程不算，它钉住的是自己的一组资源，不对应磁盘上的某个难度。
    /// 目录取 <see cref="SongFiles.PreferredChart"/> 选中的那张谱面，与点进去真正打开的是同一个难度。
    /// </summary>
    static bool TrySong(string path, out string directory, out string difficulty)
    {
        directory = difficulty = "";
        if (path.EndsWith(".sgv.json", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }
        if (Directory.Exists(path))
        {
            if (SongFiles.PreferredChart(path) is not { } chart)
            {
                return false;
            }
            directory = path;
            difficulty = SongFiles.Difficulty(chart);
            return true;
        }
        if (Path.GetExtension(path).ToLowerInvariant() is not (".vsb" or ".vsc" or ".vsm" or ".vsp"))
        {
            return false;
        }
        directory = Path.GetDirectoryName(path) ?? "";
        if (directory.Length == 0)
        {
            return false;
        }
        difficulty = SongFiles.Difficulty(path);
        return true;
    }

    /// <summary>缺项逐段省略：没有曲师就不画那一截，没有等级就不画 LV.，难度名照常显示。</summary>
    static string Compose(string name, string? artist, string? difficulty, string? level)
    {
        string text = artist is { Length: > 0 } ? name + " / " + artist : name;
        if (difficulty is { Length: > 0 })
        {
            text += " @ " + difficulty;
        }
        return level is { Length: > 0 } ? text + "  LV." + level : text;
    }

    /// <summary>原来的文件名写法：工程去掉 .sgv.json 后缀，其余保留扩展名（目录给出目录名）。</summary>
    static string FileName(string path)
    {
        string name = Path.GetFileName(Path.TrimEndingDirectorySeparator(path));
        return name.EndsWith(".sgv.json", StringComparison.OrdinalIgnoreCase) ? name[..^9] : name;
    }
}
