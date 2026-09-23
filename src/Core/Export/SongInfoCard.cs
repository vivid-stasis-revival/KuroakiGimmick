using System.Globalization;

namespace KuroakiGimmick.Core;

/// <summary>
/// 一张乐曲信息卡片所需的全部已解析数据：显示文字、封面路径与六项统计。
/// 卡片按难度成立 —— BACKSTAGE 用的是 enc_data 指定的曲名、封面与音频，因而与同一首歌的其它难度并不相同。
/// 这里只做数据装配，不碰 GPU，渲染与落盘各自分开。
/// </summary>
public sealed record SongInfoCard(string Title, string Artist, string BpmDisplay, double LengthSeconds,
    string Difficulty, string DisplayDifficulty, string? Level, string? Designer, string? JacketArtist,
    string? JacketPath, string? ChartId, int NoteCount, SongStats Stats, string GimmickSource, double GimmickWeight)
{
    /// <summary>m:ss；长度不可用时显示 ?:??，不显示一个编出来的 0:00。</summary>
    public string LengthDisplay => LengthSeconds > 0
        ? $"{SongStats.Round(LengthSeconds) / 60}:{SongStats.Round(LengthSeconds) % 60:00}" : "?:??";

    /// <summary>GIMMICK 项只有大于 0 才占一行，与原版统计面板一致。</summary>
    public bool HasGimmick => Stats.Gimmick > 0;

    /// <summary>
    /// 由当前会话装配卡片。长度优先取已解码音频的实际时长 —— 统计公式里的 length 直接决定五项数值，
    /// 用谱面末尾事件推算会与游戏显示的结果对不上；没有音频时才退回谱面时长。
    /// </summary>
    public static SongInfoCard Build(Session session)
    {
        var project = session.Project;
        string basis = project.Chart ?? project.Gimmick ?? project.Images ?? "";
        string difficulty = SongInfo.Normalize(SongFiles.Difficulty(basis));
        string? root = SongFiles.Root(project);
        var info = root == null ? null : SongInfo.Read(root);
        var view = info?.Effective(difficulty);
        // 音频时长是解码后的实际秒数；谱面时长含末尾 2 秒余量，只作为没有音频时的兜底。
        double length = session.Audio?.Duration ?? 0;
        if (!(length > 0))
        {
            length = session.Chart.Notes.Count > 0
                ? session.Chart.Notes.Max(n => Math.Max(n.Time, n.End)) : session.Duration;
        }
        var weight = ModWeights.Resolve(project.Chart ?? project.Gimmick, difficulty);
        var counts = SongStats.Measure(session.Chart.Notes, length);
        var stats = length > 0
            ? SongStats.Compute(counts, length, difficulty, info?.ChartId, weight.Weight)
            : new SongStats(0, 0, 0, 0, 0, 0, 0);
        string title = project.SongName ?? view?.Name ?? view?.FormattedName ?? session.Chart.Title;
        string artist = project.SongArtist ?? view?.Artist ?? "";
        string bpm = view?.BpmDisplay ?? project.Bpm.ToString("0.###", CultureInfo.InvariantCulture);
        return new(title, artist, bpm, length, difficulty, view?.DisplayDifficulty ?? difficulty,
            project.SongLevel ?? (view == null ? null : SongInfo.Level(view)), view?.Designer, view?.JacketArtist,
            session.Jackets.DefaultPath, info?.ChartId, counts.NoteCount, stats, weight.Source, weight.Weight);
    }
}
