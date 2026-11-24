using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using KuroakiGimmick.Native;

namespace KuroakiGimmick.Graphics;

/// <summary>二维位置与尺寸；是否为像素或逻辑单位由调用接口约定，Contains 使用左闭右开边界。</summary>
/// <param name="X">左边界。</param>
/// <param name="Y">上边界。</param>
/// <param name="W">矩形宽度。</param>
/// <param name="H">矩形高度。</param>
public readonly record struct Rect(float X, float Y, float W, float H)
{
    /// <summary>判断点是否落在含左上边界、不含右下边界的矩形内。</summary>
    /// <param name="x">待检查的横坐标。</param>
    /// <param name="y">待检查的纵坐标。</param>
    /// <returns>点位于矩形内时为 true。</returns>
    public bool Contains(float x, float y) => x >= X && y >= Y && x < X + W && y < Y + H;
}
