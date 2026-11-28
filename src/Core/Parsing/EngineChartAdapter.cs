using Engine = KenomaGMK.Core;

namespace KuroakiGimmick.Core;

/// <summary>在加载边界投影宿主公开记录；Rust 快速路径、导出和编辑器仍使用既有类型。</summary>
internal static class EngineChartAdapter
{
    internal static Chart Import(Engine.Chart source)
    {
        var chart = new Chart
        {
            Title = source.Title,
            VscDialect = (VscDialect)source.VscDialect
        };
        source.AppendNotes(chart.Notes,static n => new Note(n.Time,n.Type,n.Lane,n.End,n.Extra,n.ModExtra));
        ReplaceMods(chart, source);
        return chart;
    }

    // ReplaceMods 只替换演出轨；未在新文件中声明的对象名和 proxy 计数必须继承。
    internal static Engine.Chart ModHeaders(Chart source) => new()
    {
        ObjectName = source.ObjectName,
        Proxies = source.Proxies,
        ProxyCountDeclared = source.ProxyCountDeclared
    };

    internal static void ReplaceMods(Chart chart, Engine.Chart source)
    {
        chart.ObjectName = source.ObjectName;
        chart.Proxies = source.Proxies;
        chart.ProxyCountDeclared = source.ProxyCountDeclared;
        chart.Metadata.Clear();
        foreach (var entry in source.Metadata) chart.Metadata.Add(entry.Key, entry.Value);
        chart.Mods.Clear();
        chart.Mods.EnsureCapacity(source.Mods.Count);
        foreach (var m in source.Mods)
            chart.Mods.Add(new(m.Beat, m.Duration, m.Ease, m.From, m.To, m.Name, m.Proxy, m.Order, m.SourceLine));
        chart.PerFrame.Clear();
        chart.PerFrame.EnsureCapacity(source.PerFrame.Count);
        foreach (var f in source.PerFrame) chart.PerFrame.Add(new(f.StartBeat, f.EndBeat, f.Function));
        foreach (var d in source.Diagnostics) chart.Diagnostics.Add(new(d.Source, d.Line, d.Message, d.Error));
    }
}
