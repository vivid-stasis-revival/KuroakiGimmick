// 描述绘制项的精灵、合成阶段与属性来源，并按时间求取缓动属性。

using System.Text.Json;

namespace KuroakiGimmick.Core;

/// <summary>回调或静态覆盖层的一次绘制。Stage 表示合成边界，Depth 决定同阶段内的顺序。</summary>
public sealed class GimmickDrawing
{
    /// <summary>要绘制的精灵键。</summary>
    public string Sprite { get; set; } = "";
    /// <summary>可选的 HSV 着色配置。</summary>
    public GimmickHsvTint? HsvTint { get; set; }
    /// <summary>可选的绘制生命周期定义。</summary>
    public GimmickLifecycleDefinition? Lifecycle { get; set; }
    /// <summary>调度该绘制项的合成阶段，默认 GUI。</summary>
    public string Stage { get; set; } = GimmickStages.Gui;
    /// <summary>参与计算不透明度的 mod 名称。</summary>
    public string? AlphaMod { get; set; }
    /// <summary>只在启用音符绘制时调度此项。</summary>
    public bool NotesOnly { get; set; }
    /// <summary>起始横坐标，使用逻辑画布单位。</summary>
    public double X { get; set; }
    /// <summary>起始纵坐标，使用逻辑画布单位。</summary>
    public double Y { get; set; }
    /// <summary>原始宽度的缩放倍率。</summary>
    public double ScaleX { get; set; } = 1;
    /// <summary>原始高度的缩放倍率。</summary>
    public double ScaleY { get; set; } = 1;
    /// <summary>未应用轨道或缓动时的基础不透明度。</summary>
    public double Alpha { get; set; } = 1;
    /// <summary>实际不透明度小于或等于该值时跳过绘制。</summary>
    public double MinimumAlpha { get; set; }
    /// <summary>任一轴的绝对倍率小于或等于该值时跳过绘制。</summary>
    public double MinimumAbsScale { get; set; }
    /// <summary>未应用缓动时的旋转角。</summary>
    public double Angle { get; set; }
    /// <summary>横坐标每秒增加的逻辑距离。</summary>
    public double VelocityX { get; set; }
    /// <summary>纵坐标每秒增加的逻辑距离。</summary>
    public double VelocityY { get; set; }
    /// <summary>未绑定帧轨道时的起始帧号。</summary>
    public int Frame { get; set; }
    /// <summary>直接提供帧号的可选轨道。</summary>
    public string? FrameMod { get; set; }
    /// <summary>以歌曲绝对时间求值的属性绑定；未绑定的属性按回调经过时间计算缓动。</summary>
    public Dictionary<string, GimmickScalar> Bindings { get; set; } = [];
    /// <summary>未绑定颜色时使用的 GameMaker BGR 色值。</summary>
    public uint ColorBgr { get; set; } = 0xFFFFFF;
    /// <summary>未指定帧轨道时每秒推进的帧数。</summary>
    public double FramesPerSecond { get; set; }
    /// <summary>同一阶段内的绘制顺序：Depth 大的先绘制。</summary>
    public int Depth { get; set; }

    /// <summary>同深度的显式绘制顺序。相同值继续按原始声明/事件顺序稳定排序。</summary>
    public int SortOrder { get; set; }
    /// <summary>可见绘制寿命（秒，0..60）。与 GimmickDefinition.CallbackLifetimes 的时间轴尾部占用不是同一个量，两者不混用。</summary>
    public double Lifetime { get; set; } = 1;
    /// <summary>指定时仅响应取值相等的回调。</summary>
    public double? EventValue { get; set; }
    /// <summary>只保留同一 drawing 的最近一次触发；不吞掉其它通道或其它回调产生的绘制项。</summary>
    public bool LatestOnly { get; set; }
    /// <summary>排除触发时刻落在这些区间内的回调。</summary>
    public List<GimmickTimeRange> SuppressRanges { get; set; } = [];
    /// <summary>基于来源事件序号的可重复随机放置规则。</summary>
    public GimmickRandomPlacement? Random { get; set; }
    /// <summary>按声明顺序覆盖属性值的缓动段。</summary>
    public List<GimmickTween> Tweens { get; set; } = [];

    /// <summary>按声明顺序求值；验证阶段会稳定排序，避免每帧重复 Where/OrderBy 分配。</summary>
    /// <param name="property">查询的属性键。</param>
    /// <param name="age">距回调触发的秒数。</param>
    /// <returns>指定属性在该时刻的数值。</returns>
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
        return KenomaGMK.Runtime.Definitions.DrawingTween.Value(Tweens, property, age, value, Easings.Eval);
    }

    /// <summary>优先计算属性的时间轴绑定，未绑定时按回调年龄计算基础值与缓动。</summary>
    /// <param name="property">查询的属性键。</param>
    /// <param name="age">距回调触发的秒数。</param>
    /// <param name="timeline">用于计算绑定表达式的时间轴。</param>
    /// <param name="time">绑定求值的歌曲时刻，单位为秒。</param>
    /// <returns>指定属性在该时刻的数值。</returns>
    public double Value(string property, double age, Timeline timeline, double time) =>
        Bindings.TryGetValue(property, out var scalar) ? scalar.Value(timeline, time) : Value(property, age);
}

