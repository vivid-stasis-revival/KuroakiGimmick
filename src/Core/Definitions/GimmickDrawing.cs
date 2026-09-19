using System.Text.Json;

namespace KuroakiGimmick.Core;

/// <summary>回调或静态覆盖层的一次绘制。Stage 表示合成边界，Depth 决定同阶段内的顺序。</summary>
public sealed class GimmickDrawing
{
    public string Sprite { get; set; } = "";
    public string Stage { get; set; } = GimmickStages.Gui;
    public string? AlphaMod { get; set; }
    public bool NotesOnly { get; set; }
    public double X { get; set; }
    public double Y { get; set; }
    public double ScaleX { get; set; } = 1;
    public double ScaleY { get; set; } = 1;
    public double Alpha { get; set; } = 1;
    public double Angle { get; set; }
    public double VelocityX { get; set; }
    public double VelocityY { get; set; }
    public int Frame { get; set; }
    public double FramesPerSecond { get; set; }
    /// <summary>同一阶段内的绘制顺序：Depth 大的先绘制。</summary>
    public int Depth { get; set; }

    /// <summary>同深度的显式绘制顺序。相同值继续按原始声明/事件顺序稳定排序。</summary>
    public int SortOrder { get; set; }
    /// <summary>可见绘制寿命（秒，0..60）。与 GimmickDefinition.CallbackLifetimes 的时间轴尾部占用不是同一个量，两者不混用。</summary>
    public double Lifetime { get; set; } = 1;
    public double? EventValue { get; set; }
    /// <summary>只保留同一 drawing 的最近一次触发；不吞掉其它通道或其它回调产生的绘制项。</summary>
    public bool LatestOnly { get; set; }
    public List<GimmickTimeRange> SuppressRanges { get; set; } = [];
    public GimmickRandomPlacement? Random { get; set; }
    public List<GimmickTween> Tweens { get; set; } = [];

    /// <summary>按声明顺序求值；验证阶段会稳定排序，避免每帧重复 Where/OrderBy 分配。</summary>
    public double Value(string property, double age)
    {
        double value = property switch
        {
            "x" => X + VelocityX * age,
            "y" => Y + VelocityY * age,
            "scaleX" => ScaleX,
            "scaleY" => ScaleY,
            "angle" => Angle,
            _ => Alpha
        };
        foreach (var tween in Tweens)
        {
            if (tween.Property != property || age < tween.Delay)
            {
                continue;
            }
            value = tween.Duration <= 0 ? tween.To : tween.From + (tween.To - tween.From) * Easings.Eval(tween.Ease,
                (age - tween.Delay) / tween.Duration);
        }
        return value;
    }
}

