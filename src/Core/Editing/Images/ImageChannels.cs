namespace KuroakiGimmick.Core.Editing;

/// <summary>可直接编辑的图片属性通道。位置与缩放各含两个轴，选中一个通道就意味着两轴一起写，不存在只写半边的情况。</summary>
[Flags]
public enum ImageChannels
{
    /// <summary>没有选中的通道。</summary>
    None = 0,
    /// <summary>同时编辑两个坐标轴。</summary>
    Position = 1,
    /// <summary>同时编辑两个缩放轴。</summary>
    Scale = 2,
    /// <summary>编辑旋转角。</summary>
    Rotation = 4,
    /// <summary>编辑不透明度。</summary>
    Alpha = 8,
    /// <summary>包含所有受支持的姿态通道。</summary>
    All = 15
}
