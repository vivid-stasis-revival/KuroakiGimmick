namespace KuroakiGimmick.Core;

/// <summary>两种 memory-merge 演出共用的原版数据表。</summary>
public sealed class OperationConsoleDefinition
{
    /// <summary>区域编号对应的背景精灵键。</summary>
    public string[] AreaBackgrounds { get; set; } = [];
    /// <summary>区域编号对应的背景帧。</summary>
    public int[] AreaFrames { get; set; } = [];
    /// <summary>控制台可显示的命令文本。</summary>
    public string[] Commands { get; set; } = [];
    /// <summary>状态栏的文本表。</summary>
    public string[] BarTexts { get; set; } = [];
    /// <summary>状态栏使用的颜色表。</summary>
    public uint[] BarColors { get; set; } = [];
}
