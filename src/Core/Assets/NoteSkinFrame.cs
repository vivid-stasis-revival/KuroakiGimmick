using System.Text.Json;

namespace KuroakiGimmick.Core;

/// <summary>
/// 单个音符帧的图集区域及原点；数据坐标与逻辑显示尺寸保持分离。
/// </summary>
// 把旧的用户 vsnotes 包和较新的 UTMT 导出统一成一套 GameMaker 局部绘制矩形。
// 纹理像素尺寸刻意与逻辑精灵矩形分开：原始图集裁片可能远大于 320×180 游戏空间中的精灵，
// 而运行期已补边的导出本身就包含了 GameMaker 的包围盒。
public sealed class NoteSkinFrame
{
    public required string Key { get; init; }
    public required string File { get; init; }
    public required float LocalX { get; init; }
    public required float LocalY { get; init; }
    public required float Width { get; init; }
    public required float Height { get; init; }
    public string? SourceSprite { get; init; }
    public int SourceFrame { get; init; }
    public int? ExpectedTextureWidth { get; init; }
    public int? ExpectedTextureHeight { get; init; }
    // File 内部的归一化源矩形。多数帧使用整张纹理：
    // raw-source v2 可能只用其中一块子矩形，而紧凑版 v1 本身已经是完整的
    // GameMaker 补边帧，因此取满整张纹理。
    public float UvX { get; init; } = 0;
    public float UvY { get; init; } = 0;
    public float UvW { get; init; } = 1;
    public float UvH { get; init; } = 1;
}

