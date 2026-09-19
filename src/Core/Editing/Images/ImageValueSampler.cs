namespace KuroakiGimmick.Core.Editing;

/// <summary>
/// 只算图片属性的轻量求值器，复用正式的读取器、段落求值与 BPM 映射，因此拖动时看到的值与最终渲染一致。
/// 它不跑粒子模拟、不分配任何 GPU 或素材资源——拖手柄、输入数值都会频繁重建它，重建成本必须只有解析文本这一项。
/// </summary>
public sealed class ImageValueSampler
{
    readonly Dictionary<string, List<Timeline.Segment>> tracks = new(StringComparer.Ordinal);
    public BpmMap Bpm { get; }
    /// <summary>
    /// 按 (拍, Order) 顺序把事件铺进各自的属性轨。Order 是源文件顺序，同拍事件必须照此叠加，换个顺序结果就不同。
    /// 573613 是“取当前值”的哨兵：这里先用已铺好的段落求出该时刻的实际值再填回去，与运行时的行为保持一致。
    /// </summary>
    public ImageValueSampler(VsmDocument document, BpmMap map)
    {
        Bpm = map;
        var chart = new Chart(); VsmReader.ReplaceModsText(chart, document.Text, "image-authoring.vsm");
        foreach (var e in chart.Mods.Where(c => c.Proxy == -1 && Easings.IsKnown(c.Ease) && CustomImages.TryMod(c.Name, out _, out _))
            .OrderBy(c => c.Beat).ThenBy(c => c.Order))
        {
            CustomImages.TryMod(e.Name, out var kind, out _);
            if (!tracks.TryGetValue(e.Name, out var list)) tracks[e.Name] = list = [];
            // 段落的时间与时长一律换算成秒后再入表，Timeline 只认秒；duration 按起点 BPM 折回秒。
            double time = map.Time(e.Beat), duration = e.Duration * 60 / map.BpmAtBeat(e.Beat);
            double at = Timeline.Evaluate(list, time, CustomImages.Default(kind));
            list.Add(new(time, duration, e.From == 573613 ? at : e.From, e.To == 573613 ? at : e.To, e.Ease, e));
        }
    }
    /// <summary>求某条属性轨在某一拍的取值。该属性完全没有事件时返回 CustomImages 的出厂默认值，而不是 0。</summary>
    public double Get(string name, double beat)
    {
        CustomImages.TryMod(name, out string kind, out _);
        double initial = CustomImages.Default(kind);
        return tracks.TryGetValue(name, out var list) ? Timeline.Evaluate(list, Bpm.Time(beat), initial) : initial;
    }
    /// <summary>某张图片在某一拍的完整姿态，六个属性各取一次。</summary>
    public ImageEditPose Pose(string id, double beat) => ImageEditPose.Read(p => Get(p + "_" + id, beat));
    /// <summary>所有出现过的属性轨在某一拍的取值快照，供检视面板一次性展示。</summary>
    public Dictionary<string, double> Values(double beat) => tracks.Keys.ToDictionary(k => k, k => Get(k, beat), StringComparer.Ordinal);
}
