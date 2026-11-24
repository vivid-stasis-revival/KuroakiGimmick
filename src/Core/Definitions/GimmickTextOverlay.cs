namespace KuroakiGimmick.Core;

/// <summary>使用游戏字体图集绘制文本表中的一项，由离散 mod 索引选择。</summary>
public sealed class GimmickTextOverlay
{
    /// <summary>按离散索引选择的文字内容。</summary>
    public string[] Frames { get; set; } = [];
    /// <summary>选择当前文本项的可选轨道。</summary>
    public string? IndexMod { get; set; }
    /// <summary>调节文字不透明度的可选轨道。</summary>
    public string? AlphaMod { get; set; }
    /// <summary>文字的逻辑横坐标。</summary>
    public double X { get; set; }
    /// <summary>文字的逻辑纵坐标。</summary>
    public double Y { get; set; }
    /// <summary>文字的基础不透明度，默认 1。</summary>
    public double Alpha { get; set; } = 1;
    /// <summary>是否按中央对齐文字。</summary>
    public bool Center { get; set; }
    /// <summary>是否绘制文字描边。</summary>
    public bool Outline { get; set; }
    /// <summary>来源 GameMaker 文字颜色，按 BGR 表示。</summary>
    public uint ColorBgr { get; set; } = 0xffffff;
    /// <summary>可选的文字底部渐变色。</summary>
    public uint? BottomColorBgr { get; set; }
    /// <summary>文字绘制的统一缩放倍率。</summary>
    public double Scale { get; set; } = 1;
    /// <summary>参与图层排序的绘制深度。</summary>
    public int Depth { get; set; }
    /// <summary>相同深度时的绘制顺序。</summary>
    public int SortOrder { get; set; }
    /// <summary>描边使用的 GameMaker BGR 颜色。</summary>
    public uint OutlineColorBgr { get; set; }
}
