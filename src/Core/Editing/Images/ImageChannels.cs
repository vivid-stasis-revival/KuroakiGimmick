namespace KuroakiGimmick.Core.Editing;

/// <summary>可直接编辑的图片属性通道。位置与缩放各含两个轴，选中一个通道就意味着两轴一起写，不存在只写半边的情况。</summary>
[Flags]
public enum ImageChannels { None = 0, Position = 1, Scale = 2, Rotation = 4, Alpha = 8, All = 15 }
