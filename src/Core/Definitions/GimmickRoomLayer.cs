namespace KuroakiGimmick.Core;

/// <summary>按图层深度从大到小合成的 GameMaker 房间背景。</summary>
public sealed class GimmickRoomLayer
{
    /// <summary>来源房间图层名。</summary>
    public string Name { get; set; } = "";
    /// <summary>随本图层调度的绘制项。</summary>
    public List<GimmickDrawing> Drawings { get; set; } = [];
    /// <summary>为离散选帧提供触发时刻的事件键。</summary>
    public string? FrameSelectionEvent { get; set; }
    /// <summary>重现离散选帧时采用的种子。</summary>
    public uint FrameSelectionSeed { get; set; }
    /// <summary>可选的房间背景精灵键。</summary>
    public string? Sprite { get; set; }
    /// <summary>图层深度，按深度从大到小合成。</summary>
    public int Depth { get; set; }
    /// <summary>与基础不透明度相乘的轨道名。</summary>
    public string? AlphaMod { get; set; }
    /// <summary>控制可见性的轨道，值小于 0.5 或非有限时隐藏。</summary>
    public string? VisibleMod { get; set; }
    /// <summary>直接指定精灵帧号的可选轨道。</summary>
    public string? FrameMod { get; set; }
    /// <summary>未指定帧轨道时按歌曲秒数推进的帧率。</summary>
    public double FramesPerSecond { get; set; }
    /// <summary>动态背景按该轨道的帧率累计推进，保留此前变速产生的帧位移。</summary>
    public string? FrameSpeedMod { get; set; }
    /// <summary>可选的图层滤镜配置。</summary>
    public GimmickShaderMode? Effect { get; set; }
    /// <summary>在该深度对已合成画面应用滤镜的效果层，本层不额外绘制精灵或底色。</summary>
    public bool FilterOnly { get; set; }
    /// <summary>按属性名覆盖位置、帧或透明度的标量绑定。</summary>
    public Dictionary<string, GimmickScalar> Bindings { get; set; } = [];
    /// <summary>与轨道及绑定共同计算的基础不透明度。</summary>
    public double Alpha { get; set; } = 1;
    /// <summary>原 GameMaker 颜色值的低 24 位，按 BGR 解释。</summary>
    public uint ColorBgr { get; set; } = 0xFFFFFF;
    /// <summary>是否拉伸背景精灵。</summary>
    public bool Stretch { get; set; }
    /// <summary>是否沿纵向重复铺设精灵。</summary>
    public bool TileVertical { get; set; }
    /// <summary>每秒增加的逻辑纵向偏移。</summary>
    public double ScrollY { get; set; }
    /// <summary>房间层在 320×180 逻辑坐标中的水平偏移。</summary>
    public double OffsetX { get; set; }
    /// <summary>在 320×180 逻辑画布上的初始纵向偏移。</summary>
    public double OffsetY { get; set; }
    /// <summary>每秒增加的逻辑横向偏移。</summary>
    public double ScrollX { get; set; }
    /// <summary>是否沿横向重复铺设精灵。</summary>
    public bool TileHorizontal { get; set; }
    /// <summary>未指定可见性轨道时采用的开关。</summary>
    public bool Visible { get; set; } = true;
}
