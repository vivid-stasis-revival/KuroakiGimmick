namespace KuroakiGimmick.Core.Editing;

/// <summary>
/// 用正式的段落求值与 BPM 规则算文本属性的取值，不构建场景、不分配任何资源；输入数值与拖拽时会频繁重建它。
/// </summary>
public sealed class TextValueSampler
{
    readonly Dictionary<string, List<Timeline.Segment>> tracks = new(StringComparer.Ordinal);
    readonly BpmMap map;
    /// <summary>
    /// 按 (拍, Order) 把事件铺进各自的属性轨；Order 是源文件顺序，同拍事件必须照此叠加。
    /// 573613 是“取当前值”的哨兵，这里先由已铺好的段落求出该时刻的实际值再填回去，与运行时行为一致。
    /// </summary>
    public TextValueSampler(VsmDocument document, BpmMap map)
    {
        this.map = map;
        var chart = new Chart(); VsmReader.ReplaceModsText(chart, document.Text, "text-authoring.vsm");
        foreach (var e in chart.Mods.Where(e => e.Proxy == -1 && CustomText.TryMod(e.Name, out _, out _) && Easings.IsKnown(e.Ease))
            .OrderBy(e => e.Beat).ThenBy(e => e.Order))
        {
            if (!tracks.TryGetValue(e.Name, out var list)) tracks[e.Name] = list = [];
            // Timeline 只认秒：拍先经 BPM map 换成秒，duration 按起点 BPM 折回秒。
            double time = map.Time(e.Beat), at = Timeline.Evaluate(list, time, ModCatalog.Default(e.Name, -1));
            list.Add(new(time, e.Duration * 60 / map.BpmAtBeat(e.Beat), e.From == 573613 ? at : e.From,
                e.To == 573613 ? at : e.To, e.Ease, e));
        }
    }
    /// <summary>求某条属性轨在某一拍的取值；该属性没有任何事件时返回 ModCatalog 的默认值，而不是 0。</summary>
    public double Get(string name, double beat) => tracks.TryGetValue(name, out var list)
        ? Timeline.Evaluate(list, map.Time(beat), ModCatalog.Default(name, -1)) : ModCatalog.Default(name, -1);
    public double Get(string kind, string id, double beat) => Get(Name(kind, id), beat);
    /// <summary>拼出 mod 名。ID 为空表示旧版无名文本，此时不加后缀——加了就变成另一条轨。</summary>
    public static string Name(string kind, string id) => kind + (id.Length == 0 ? "" : "_" + id);
}
