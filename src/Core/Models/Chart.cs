using System.Text.Json;

namespace KuroakiGimmick.Core;

/// <summary>
/// 文本 VSC 的源格式。Legacy 表示没有出现 CSM 3.4.0 的第五列；Csm340 表示至少一条有效数据行带第五列。
/// 这是源文档属性，不是“当前 K/G 支持的最高版本”：导出必须跟随它，不能自动把旧谱升级成新格式。
/// </summary>
public enum VscDialect
{
    /// <summary>有效数据行均未声明第五列。</summary>
    Legacy,
    /// <summary>至少一条有效数据行含有第五列，即使其内容为空。</summary>
    Csm340
}

/// <summary>
/// 解析后的谱面数据。音符时间用秒，演出事件时间用拍；诊断保留源文件与行号，不通过删除未知事件假装兼容。
/// </summary>
public sealed class Chart
{
    /// <summary>按默认标题、演出对象和空轨道创建谱面。</summary>
    public Chart()
    {
    }

    // LIFETIME: 会话复用音符等记录，独立持有列表、标识符集合和元数据字典。
    internal Chart CopyForSession()
    {
        var copy = new Chart
        {
            Title = Title,
            ObjectName = ObjectName,
            Proxies = Proxies,
            ProxyCountDeclared = ProxyCountDeclared,
            VscDialect = VscDialect
        };
        copy.Notes.AddRange(Notes);
        copy.Mods.AddRange(Mods);
        copy.PerFrame.AddRange(PerFrame);
        copy.Diagnostics.AddRange(Diagnostics);
        copy.ImageNames.UnionWith(ImageNames);
        copy.TextNames.UnionWith(TextNames);
        foreach (var entry in Metadata) copy.Metadata.Add(entry.Key, entry.Value);
        return copy;
    }
    /// <summary>谱面标题，未指定时为 UNTITLED。</summary>
    public string Title { get; set; } = "UNTITLED";
    /// <summary>驱动该谱面的演出对象名，默认使用 obj_base_gimmick。</summary>
    public string ObjectName { get; set; } = "obj_base_gimmick";
    /// <summary>谱面的 proxy 数量，缺省时取 1。</summary>
    public int Proxies { get; set; } = 1;
    /// <summary>源数据是否显式声明了 proxy 数量。缺省值 1 只是解析/编辑回退，不能据此凭空写出 !proxies:1。</summary>
    public bool ProxyCountDeclared { get; set; }
    /// <summary>谱面的音符与变速记录，时间以秒计。</summary>
    public List<Note> Notes { get; } = [];
    /// <summary>按拍定位的演出事件，保留来源顺序信息。</summary>
    public List<ModEvent> Mods { get; } = [];
    /// <summary>指定拍区间内执行的逐帧函数。</summary>
    public List<FrameEvent> PerFrame { get; } = [];
    /// <summary>读取谱面及演出配置时累积的诊断。</summary>
    public List<Diagnostic> Diagnostics { get; } = [];
    /// <summary>已登记的图片标识符集合。</summary>
    public HashSet<string> ImageNames { get; } = [];
    /// <summary>已登记的文本标识符集合。</summary>
    public HashSet<string> TextNames { get; } = [];
    /// <summary>按区分大小写的键保存的谱面元数据。</summary>
    public Dictionary<string, string> Metadata { get; } = new(StringComparer.Ordinal);
    /// <summary>
    /// 打开 VSC 时从源文本检测出的 dialect。默认 Legacy；VSB 或无谱面会保持默认值。
    /// 只要源文件任何有效数据行明确带第五列就记为 Csm340，即便该列为空。
    /// </summary>
    public VscDialect VscDialect { get; set; } = VscDialect.Legacy;
    /// <summary>谱面秒数：最后一个音符（含 hold 结束）之后再留 2 秒余量，且不短于 1 秒；不含演出回调的尾部占用。</summary>
    public double Duration => Math.Max(1, Notes.Count == 0 ? 0 : Notes.Max(n => Math.Max(n.Time, n.End)) + 2);
}

