using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using KuroakiGimmick.Native;

namespace KuroakiGimmick.Graphics;

/// <summary>非预乘 RGBA 浮点颜色。透明度与 RGB 分开保留，混合规则由 Canvas/GPU 管线指定。</summary>
public readonly record struct Color(float R, float G, float B, float A = 1)
{
    public static Color Hex(uint rgb, float alpha = 1) => new(((rgb >> 16) & 255) / 255f, ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f, alpha);
    public Color Alpha(double a) => this with
    {
        A = (float)Math.Clamp(a, 0, 1)
    };
    public static Color White => new(1, 1, 1);
    /// <summary>色相 h 与饱和度 s 均为 0..1；明度恒为 1，需要变暗请再乘颜色或调 alpha。</summary>
    public static Color Hsv(float h, float s)
    {
        float F(float n)
        {
            var k = (n + h * 6) % 6;
            return 1 - s * Math.Max(0, Math.Min(Math.Min(k, 4 - k), 1));
        }
        return new(F(5), F(3), F(1));
    }
}
