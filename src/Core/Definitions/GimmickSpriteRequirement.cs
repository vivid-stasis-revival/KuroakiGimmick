using System.Text.Json;

namespace KuroakiGimmick.Core;

/// <summary>可选的资源契约；尺寸是资源数据，不应写进某个歌曲专用加载函数。</summary>
public sealed class GimmickSpriteRequirement
{
    /// <summary>要求的源帧宽度，单位为像素。</summary>
    public int Width { get; set; }
    /// <summary>要求的源帧高度，单位为像素。</summary>
    public int Height { get; set; }
    /// <summary>所要求的精灵帧数。</summary>
    public int Frames { get; set; }
}

