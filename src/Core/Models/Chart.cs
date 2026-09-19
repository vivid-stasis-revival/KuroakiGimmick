using System.Text.Json;

namespace KuroakiGimmick.Core;

/// <summary>
/// 解析后的谱面数据。音符时间用秒，演出事件时间用拍；诊断保留源文件与行号，不通过删除未知事件假装兼容。
/// </summary>
public sealed class Chart
{
    public string Title { get; set; } = "UNTITLED";
    public string ObjectName { get; set; } = "obj_base_gimmick";
    public int Proxies { get; set; } = 1;
    public List<Note> Notes { get; } = [];
    public List<ModEvent> Mods { get; } = [];
    public List<FrameEvent> PerFrame { get; } = [];
    public List<Diagnostic> Diagnostics { get; } = [];
    public HashSet<string> ImageNames { get; } = [];
    public HashSet<string> TextNames { get; } = [];
    public Dictionary<string, string> Metadata { get; } = new(StringComparer.Ordinal);
    /// <summary>谱面秒数：最后一个音符（含 hold 结束）之后再留 2 秒余量，且不短于 1 秒；不含演出回调的尾部占用。</summary>
    public double Duration => Math.Max(1, Notes.Count == 0 ? 0 : Notes.Max(n => Math.Max(n.Time, n.End)) + 2);
}

