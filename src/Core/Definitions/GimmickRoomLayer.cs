namespace KuroakiGimmick.Core;

/// <summary>One native GameMaker room background, ordered by decreasing layer depth.</summary>
public sealed class GimmickRoomLayer
{
    public string Name { get; set; } = "";
    public List<GimmickDrawing> Drawings { get; set; } = [];
    public string? FrameSelectionEvent { get; set; }
    public uint FrameSelectionSeed { get; set; }
    public string? Sprite { get; set; }
    public int Depth { get; set; }
    public string? AlphaMod { get; set; }
    public string? VisibleMod { get; set; }
    public string? FrameMod { get; set; }
    public double FramesPerSecond { get; set; }
    /// <summary>Animated source backgrounds accumulate this frame rate, including speed changes.</summary>
    public string? FrameSpeedMod { get; set; }
    public GimmickShaderMode? Effect { get; set; }
    /// <summary>A room effect layer filters the composition accumulated at its depth, without drawing a sprite or color.</summary>
    public bool FilterOnly { get; set; }
    public Dictionary<string, GimmickScalar> Bindings { get; set; } = [];
    public double Alpha { get; set; } = 1;
    /// <summary>Original GameMaker BGR color, low 24 bits; do not treat as RGB.</summary>
    public uint ColorBgr { get; set; } = 0xFFFFFF;
    public bool Stretch { get; set; }
    public bool TileVertical { get; set; }
    public double ScrollY { get; set; }
    /// <summary>Room layer offsets in the 320x180 GameMaker logical coordinate system.</summary>
    public double OffsetX { get; set; }
    public double OffsetY { get; set; }
    public double ScrollX { get; set; }
    public bool TileHorizontal { get; set; }
    public bool Visible { get; set; } = true;
}
