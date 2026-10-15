using System.Text.Json;

namespace KuroakiGimmick.Core;

/// <summary>精灵的源帧尺寸和原点；Frames 中保存相对路径，加载后的副本保存绝对路径。</summary>
public sealed class GimmickSprite
{
    public int Width { get; set; }
    public int Height { get; set; }
    public int OriginX { get; set; }
    public int OriginY { get; set; }
    public string? Scope { get; set; }
    /// <summary>Large source banks are header-validated eagerly and decoded on demand under the shared GPU LRU budget.</summary>
    public bool Lazy { get; set; }
    public List<string> Frames { get; set; } = [];
}

