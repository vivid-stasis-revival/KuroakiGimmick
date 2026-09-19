using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using KuroakiGimmick.Native;

namespace KuroakiGimmick.Graphics;

/// <summary>二维位置与尺寸；是否为像素或逻辑单位由调用接口约定，Contains 使用左闭右开边界。</summary>
public readonly record struct Rect(float X, float Y, float W, float H)
{
    public bool Contains(float x, float y) => x >= X && y >= Y && x < X + W && y < Y + H;
}
