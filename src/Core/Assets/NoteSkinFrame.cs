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
    /// <summary>皮肤内用于查找该帧的键。</summary>
    public required string Key { get; init; }
    /// <summary>提供图像的纹理文件。</summary>
    public required string File { get; init; }
    /// <summary>绘制矩形的局部横坐标，使用逻辑画布单位。</summary>
    public required float LocalX { get; init; }
    /// <summary>绘制矩形的局部纵坐标，使用逻辑画布单位。</summary>
    public required float LocalY { get; init; }
    /// <summary>绘制矩形的逻辑宽度。</summary>
    public required float Width { get; init; }
    /// <summary>绘制矩形的逻辑高度。</summary>
    public required float Height { get; init; }
    /// <summary>导出记录中可选的原版精灵名。</summary>
    public string? SourceSprite { get; init; }
    /// <summary>对应的原版精灵帧索引。</summary>
    public int SourceFrame { get; init; }
    /// <summary>校验纹理时预期的像素宽度，可省略。</summary>
    public int? ExpectedTextureWidth { get; init; }
    /// <summary>校验纹理时预期的像素高度，可省略。</summary>
    public int? ExpectedTextureHeight { get; init; }
    // File 内部的归一化源矩形。多数帧使用整张纹理：
    // raw-source v2 可能只用其中一块子矩形，而紧凑版 v1 本身已经是完整的
    // GameMaker 补边帧，因此取满整张纹理。
    /// <summary>帧在纹理中的归一化左边界。</summary>
    public float UvX { get; init; } = 0;
    /// <summary>帧在纹理中的归一化上边界。</summary>
    public float UvY { get; init; } = 0;
    /// <summary>帧占纹理宽度的比例。</summary>
    public float UvW { get; init; } = 1;
    /// <summary>帧占纹理高度的比例。</summary>
    public float UvH { get; init; } = 1;
}

