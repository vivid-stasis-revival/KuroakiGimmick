using System.Runtime.InteropServices;
using KuroakiGimmick.Native;
using static KuroakiGimmick.Native.Sdl;

namespace KuroakiGimmick.Graphics;

/// <summary>本项目实际使用的 SDL_GPUBlendFactor 原生数值；不是 OpenGL 常量。</summary>
public enum BlendFactor : uint
{
    Zero = 1,
    One = 2,
    DestinationColor = 5,
    InverseDestinationColor = 6,
    SourceAlpha = 7,
    InverseSourceAlpha = 8
}
