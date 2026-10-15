namespace KuroakiGimmick.Core;

/// <summary>Source tables shared by the two memory-merge presentations.</summary>
public sealed class OperationConsoleDefinition
{
    public string[] AreaBackgrounds { get; set; } = [];
    public int[] AreaFrames { get; set; } = [];
    public string[] Commands { get; set; } = [];
    public string[] BarTexts { get; set; } = [];
    public uint[] BarColors { get; set; } = [];
}
