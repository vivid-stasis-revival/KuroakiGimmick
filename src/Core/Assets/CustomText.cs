// 关联字幕 ID 与文本文件，并根据起始拍选择生效的提示。

using SharedCues = KenomaGMK.Core.TextCues;

namespace KuroakiGimmick.Core;

/// <summary>
/// 歌词与自定义文字轨道。声明名、文本内容和时间位置分别处理，查找失败必须可诊断。
/// </summary>
// 文本内容按拍索引；位置与外观是普通的 VSM 轨道。
public sealed class CustomText
{
    /// <summary>在指定拍生效的字幕。</summary>
    /// <param name="Beat">起始拍数。</param>
    /// <param name="Text">字幕文本。</param>
    public record Cue(double Beat, string Text) : KenomaGMK.Core.ITextCue;
    /// <summary>以 ID 寻址的字幕文件和提示集合。</summary>
    /// <param name="Id">字幕轨道 ID。</param>
    /// <param name="Path">字幕文件路径。</param>
    /// <param name="Cues">轨道的字幕提示。</param>
    /// <param name="SourceText">保留供编辑使用的源文本。</param>
    public sealed record Track(string Id, string Path, List<Cue> Cues, string SourceText = "")
    {
        /// <summary>二分取该拍之前最后一条 cue；第一条 cue 之前返回空串，不回绕到末尾。</summary>
        /// <param name="beat">谱面位置，单位为拍。</param>
        /// <returns>该拍已生效的字幕内容。</returns>
        public string At(double beat) => SharedCues.At(Cues, beat);

    }

    /// <summary>已加载的字幕轨道及提示内容。</summary>
    public List<Track> Tracks { get; } = [];
    /// <summary>按公共字幕属性表拆分属性和 ID。</summary>
    public static bool TryMod(string name, out string kind, out string id) => SharedCues.TryMod(name, out kind, out id);
    /// <summary>返回旧式或有 ID 字幕轨道的原版默认值。</summary>
    public static double Default(string kind, bool legacy = false) => SharedCues.Default(kind, legacy);
    /// <summary>读取工程中的字幕源并构建按拍查询的轨道。</summary>
    /// <param name="p">当前工程的路径与呈现配置。</param>
    /// <param name="chart">提供音符、演出事件及诊断的谱面。</param>
    /// <param name="edited">按字幕 ID 提供的编辑文本。</param>
    /// <param name="enabled">是否启用字幕加载。</param>
    /// <returns>解析得到的字幕轨道集合。</returns>
    public static CustomText Load(ViewerProject p, Chart chart, IReadOnlyDictionary<string, string>? edited = null, bool? enabled = null)
    {
        var result = new CustomText();
        var parsed = new KenomaGMK.Core.Chart();
        var tracks = SongFiles.Resources(p).LoadTexts(parsed, static (beat, text) => new Cue(beat, text),
            ChartLoadLimits.Unlimited, edited, enabled);
        foreach (var track in tracks) result.Tracks.Add(new(track.Id, track.Path, track.Cues, track.SourceText));
        chart.TextNames.UnionWith(parsed.TextNames);
        foreach (var d in parsed.Diagnostics) chart.Diagnostics.Add(new(d.Source, d.Line, d.Message, d.Error));
        return result;
    }

    /// <summary>检查轨道是否引用已声明的字幕。</summary>
    /// <param name="name">完整字幕属性名。</param>
    /// <returns>引用已声明轨道时为 true。</returns>
    public bool IsDeclaredMod(string name)
    {
        if (!TryMod(name, out var kind, out var id))
        {
            return false;
        }
        return Tracks.Any(t => t.Id == id || kind is "textX" or "textY" && id == t.Id + "b");
    }
}
