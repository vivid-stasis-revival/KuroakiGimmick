using System.Text.Json;

namespace KuroakiGimmick.Core;

/// <summary>可选的资源契约；尺寸是资源数据，不应写进某个歌曲专用加载函数。</summary>
public sealed class GimmickSpriteRequirement
{
    public int Width { get; set; }
    public int Height { get; set; }
    public int Frames { get; set; }
}

