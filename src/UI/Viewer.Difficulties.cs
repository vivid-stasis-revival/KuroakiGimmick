using System.Runtime.InteropServices;
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

    /// <summary>info.json / song.json / shatterinfo.json 本身也能作为主文件拖入，判断与 SongFiles.Open 共用一处。</summary>
    static bool SongInfoFile(string path) => SongInfo.IsInfoFile(path);

    /// <summary>
    /// 把 shatterinfo.json 当作真实的拖放事件丢进窗口，走的是用户拖文件的同一条路：
    /// Handle → LoadPaths 挑主文件 → 后台 Session.Load → Update 切换会话。命令行和 CPU 自测只覆盖最后那段，
    /// 这里补的是"文件被当成附加资源塞给当前歌、而不是当成要打开的歌"这种只在窗口里才出现的失败。
    /// 同目录另放一张 FINALE：落到 SHATTER 才说明认的是这份文件，不是按目录优先级碰巧选中。
    /// 结束时换回原会话，后面的断言照旧跑在原来那首歌上。
    /// </summary>
    public void SmokeShatterDrop()
    {
        var original = Current;
        string dir = Path.Combine(Path.GetTempPath(), "kuroaki-shatter-drop-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        nint data = 0;
        try
        {
            foreach (string level in new[] { "FINALE", "SHATTER" })
            {
                File.WriteAllText(Path.Combine(dir, level + ".vsc"), "0,3,0,b:245|t:0|v:undefined|s:undefined\n1000,0,0\n");
            }
            // CSM treats SHATTER as an independent song entry. Even when info.json explicitly points at main-song media,
            // a SHATTER file that omits those fields must fall back to the directory defaults rather than inherit them.
            File.WriteAllText(Path.Combine(dir, "info.json"),
                "{\"audio_id\":\"main.ogg\",\"jacket\":\"main.png\",\"preview_id\":\"main-preview.ogg\",\"jacket_animated\":true}");
            string file = Path.Combine(dir, SongInfo.ShatterFile);
            File.WriteAllText(file, "{\"name\":\"Drop Song [Shatter]\",\"difficulty_number\":\"15+\",\"note_designer\":\"drop\"}");
            var shatterView = SongInfo.Read(dir)?.Effective("SHATTER")
                ?? throw new InvalidOperationException("SHATTER song info was not readable.");
            if (shatterView.AudioId != null || shatterView.Jacket != null || shatterView.PreviewId != null || shatterView.JacketAnimated)
                throw new InvalidOperationException("SHATTER incorrectly inherited main-song media metadata.");
            data = Marshal.StringToCoTaskMemUTF8(file);
            Handle(new() { Type = 0x1000, DropData = data });
            if (loading == null)
            {
                throw new InvalidOperationException("Dropping shatterinfo.json did not start loading a song: " + message);
            }
            if (!loading.Wait(TimeSpan.FromSeconds(30)))
            {
                throw new InvalidOperationException("Loading the dropped shatterinfo.json did not finish.");
            }
            Update();
            if (Current == original || Current.Project.Chart is not { } chart
                || !Path.GetFileName(chart).Equals("SHATTER.vsc", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Dropping shatterinfo.json did not open its SHATTER chart: " + message);
            }
            if (Current.Project.Title != "Drop Song [Shatter] / SHATTER" || DifficultyLabel != "SHATTER 15+")
            {
                throw new InvalidOperationException(
                    $"The dropped SHATTER chart lost its song info: \"{Current.Project.Title}\" / \"{DifficultyLabel}\".");
            }
        }
        finally
        {
            if (data != 0)
            {
                Marshal.FreeCoTaskMem(data);
            }
            if (Current != original)
            {
                // 自测宿主是 silent 启动的：不挂音频，也不套用户偏好。换回时必须照原样 silent，否则
                // 保存着的偏好（比如 Monaco 字体）会被写进原来那首歌的 project，后面的断言就跑在一个被改过的会话上。
                UseSession(original, silent: true);
                ResetEditorForLoad();
            }
            try
            {
                Directory.Delete(dir, true);
            }
            catch (IOException)
            {
            }
        }
    }

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
