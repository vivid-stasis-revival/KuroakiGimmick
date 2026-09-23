using KuroakiGimmick.Core;
using KuroakiGimmick.Graphics;

namespace KuroakiGimmick.UI;

/// <summary>
/// 难度切换。歌曲目录带 info.json 时列出磁盘上真实存在的难度，切换等价于打开对应的谱面文件 ——
/// 音频、封面、VSM 与 BPM 都由 SongFiles 重新解析，BACKSTAGE 因此会换成 enc_data 指定的那一套。
/// </summary>
public sealed partial class Viewer
{
    SongFiles.SongDifficulty[] difficulties = [];
    string activeDifficulty = "";

    /// <summary>info.json / song.json 本身也能作为主文件打开，等同于打开它所在的歌曲目录。</summary>
    static bool SongInfoFile(string path) => Path.GetFileName(path).ToLowerInvariant() is "info.json" or "song.json";

    /// <summary>
    /// 只有多于一个难度时才值得占据左栏那一行。已打开 .sgv.json 工程时不提供切换：
    /// 工程钉住了自己的谱面、图片、字幕与标记，换难度等于丢掉它们。
    /// </summary>
    bool DifficultyBarVisible => difficulties.Length > 1 && Current.ProjectPath == null;

    /// <summary>
    /// 当前难度在 UI 上的写法，例如 "BACKSTAGE 17"。谱面文件名是 ENCORE 而 UI 叫 BACKSTAGE 是正常的，
    /// 这里显示的是后者；只有一个难度时难度条不出现，这行仍然是唯一能看出当前难度的地方。
    /// </summary>
    string DifficultyLabel
    {
        get
        {
            var active = difficulties.FirstOrDefault(d => SongInfo.Normalize(d.Name) == activeDifficulty);
            return active == null ? activeDifficulty
                : active.Level is { Length: > 0 } level ? active.Display + " " + level : active.Display;
        }
    }

    /// <summary>会话切换后重算一次；磁盘枚举不放进 Draw，每帧扫目录会把左栏变成 IO 循环。</summary>
    void RefreshDifficulties()
    {
        difficulties = [];
        activeDifficulty = "";
        string? basis = Current.Project.Chart ?? Current.Project.Gimmick;
        string? root = basis == null ? null : Path.GetDirectoryName(basis);
        if (root == null || !Directory.Exists(root))
        {
            return;
        }
        try
        {
            var info = SongInfo.Read(root);
            if (info == null)
            {
                return;
            }
            difficulties = SongFiles.Difficulties(root, info);
            activeDifficulty = SongInfo.Normalize(SongFiles.Difficulty(basis!));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            difficulties = [];
        }
    }

    /// <summary>
    /// 切到另一个难度：直接按目标谱面重新打开，而不是在当前工程上改路径。
    /// 难度之间的音频、封面和演出文件都可能不同，半路替换其中一项会得到一个不存在的组合。
    /// </summary>
    void SwitchDifficulty(SongFiles.SongDifficulty target)
    {
        if (target.Chart == null || SongInfo.Normalize(target.Name) == activeDifficulty)
        {
            return;
        }
        if (GuardUnsaved(() => SwitchDifficulty(target))) return;
        if (Busy)
        {
            return;
        }
        transport.SetPlaying(false);
        string chart = target.Chart;
        loading = Task.Run(() => Session.Load(chart));
        message = L.Format($"Loading {target.Display}...");
    }

    /// <summary>
    /// 左栏的难度条。画在原本放拖放提示的那一行（标签 + 一排 26 px 方块，正好停在分隔线之前），
    /// 这样下方所有固定坐标都不用整体下移。难度名不在这里重复，它由下面的 CHART 行显示。
    /// 难度槽有信息但目录里没有谱面文件时保留为不可点的占位，不假装它可以打开。
    /// </summary>
    void DrawDifficulties(float x, float y, float w)
    {
        Label(L.Get("DIFFICULTY"), x, y);
        float gap = 4, cw = Math.Max(30, (w - gap * (difficulties.Length - 1)) / difficulties.Length);
        for (int i = 0; i < difficulties.Length; i++)
        {
            var d = difficulties[i];
            var r = new Rect(x + i * (cw + gap), y + 13, cw, 26);
            // 标签优先显示等级数字，等级缺失时退回难度名的前三个字母，宁可短也不要画成一片糊。
            string label = d.Level is { Length: > 0 } level ? level : d.Display[..Math.Min(3, d.Display.Length)];
            if (Button(label, r, active: SongInfo.Normalize(d.Name) == activeDifficulty, enabled: !Busy && d.Chart != null,
                key: "difficulty:" + d.Name))
            {
                SwitchDifficulty(d);
            }
        }
    }
}
