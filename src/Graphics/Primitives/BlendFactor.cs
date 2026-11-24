using System.Runtime.InteropServices;
using KuroakiGimmick.Native;
using static KuroakiGimmick.Native.Sdl;

namespace KuroakiGimmick.Graphics;

/// <summary>本项目实际使用的 SDL_GPUBlendFactor 原生数值；不是 OpenGL 常量。</summary>
public enum BlendFactor : uint
{
    /// <summary>将该分量的贡献清零。</summary>
    Zero = 1,
    /// <summary>保留该分量的完整贡献。</summary>
    One = 2,
    /// <summary>以一减源颜色作为系数，原版 Starcrashers 遮罩烟花使用此模式。</summary>
    InverseSourceColor = 4,
    /// <summary>以目标颜色作为系数。</summary>
    DestinationColor = 5,
    /// <summary>以一减目标颜色作为系数。</summary>
    InverseDestinationColor = 6,
    /// <summary>以源 alpha 作为系数。</summary>
    SourceAlpha = 7,
    /// <summary>以一减源 alpha 作为系数。</summary>
    InverseSourceAlpha = 8
}
