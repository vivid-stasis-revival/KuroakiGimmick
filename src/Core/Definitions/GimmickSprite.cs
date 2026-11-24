using System.Text.Json;

namespace KuroakiGimmick.Core;

/// <summary>精灵的源帧尺寸和原点；Frames 中保存相对路径，加载后的副本保存绝对路径。</summary>
public sealed class GimmickSprite
{
    /// <summary>单帧源图像的宽度，单位为像素。</summary>
    public int Width { get; set; }
    /// <summary>单帧源图像的高度，单位为像素。</summary>
    public int Height { get; set; }
    /// <summary>绘制原点在源帧内的横坐标。</summary>
    public int OriginX { get; set; }
    /// <summary>绘制原点在源帧内的纵坐标。</summary>
    public int OriginY { get; set; }
    /// <summary>解析帧路径时使用的资源作用域。</summary>
    public string? Scope { get; set; }
    /// <summary>先检查源帧文件头，在共享 GPU LRU 预算内按需解码大型帧库。</summary>
    public bool Lazy { get; set; }
    /// <summary>按播放顺序排列的帧路径。</summary>
    public List<string> Frames { get; set; } = [];
}

