using System.Text.Json;

namespace KuroakiGimmick.Core;

/// <summary>
/// 文本 VSC 的源格式。Legacy 表示没有出现 CSM 3.4.0 的第五列；Csm340 表示至少一条有效数据行带第五列。
/// 这是源文档属性，不是“当前 K/G 支持的最高版本”：导出必须跟随它，不能自动把旧谱升级成新格式。
/// </summary>
public enum VscDialect { Legacy, Csm340 }

/// <summary>
/// 解析后的谱面数据。音符时间用秒，演出事件时间用拍；诊断保留源文件与行号，不通过删除未知事件假装兼容。
/// </summary>
public sealed class Chart
{
    public string Title { get; set; } = "UNTITLED";
    public string ObjectName { get; set; } = "obj_base_gimmick";
    public int Proxies { get; set; } = 1;
    /// <summary>源数据是否显式声明了 proxy 数量。缺省值 1 只是解析/编辑回退，不能据此凭空写出 !proxies:1。</summary>
    public bool ProxyCountDeclared { get; set; }
    public List<Note> Notes { get; } = [];
    public List<ModEvent> Mods { get; } = [];
    public List<FrameEvent> PerFrame { get; } = [];
    public List<Diagnostic> Diagnostics { get; } = [];
    public HashSet<string> ImageNames { get; } = [];
    public HashSet<string> TextNames { get; } = [];
    public Dictionary<string, string> Metadata { get; } = new(StringComparer.Ordinal);
    /// <summary>
    /// 打开 VSC 时从源文本检测出的 dialect。默认 Legacy；VSB 或无谱面会保持默认值。
    /// 只要源文件任何有效数据行明确带第五列就记为 Csm340，即便该列为空。
    /// </summary>
    public VscDialect VscDialect { get; set; } = VscDialect.Legacy;
    /// <summary>谱面秒数：最后一个音符（含 hold 结束）之后再留 2 秒余量，且不短于 1 秒；不含演出回调的尾部占用。</summary>
    public double Duration => Math.Max(1, Notes.Count == 0 ? 0 : Notes.Max(n => Math.Max(n.Time, n.End)) + 2);
}

