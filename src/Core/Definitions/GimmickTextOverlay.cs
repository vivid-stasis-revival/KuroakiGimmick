namespace KuroakiGimmick.Core;

/// <summary>Source text-table drawing, using the game's font atlas and a discrete mod index.</summary>
public sealed class GimmickTextOverlay
{
    public string[] Frames { get; set; } = [];
    public string? IndexMod { get; set; }
    public string? AlphaMod { get; set; }
    public double X { get; set; }
    public double Y { get; set; }
    public double Alpha { get; set; } = 1;
    public bool Center { get; set; }
    public bool Outline { get; set; }
    public uint ColorBgr { get; set; } = 0xffffff;
    public uint? BottomColorBgr { get; set; }
    public double Scale { get; set; } = 1;
    public int Depth { get; set; }
    public int SortOrder { get; set; }
    public uint OutlineColorBgr { get; set; }
}
