using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using KuroakiGimmick.Native;

namespace KuroakiGimmick.Graphics;

/// <summary>二维几何到固定 8-float 顶点流的转换，不读取谱面身份或对象类型。</summary>
public sealed partial class Canvas : IDisposable
{
    /// <summary>写入一个 8 float / 32 字节顶点：x,y,u,v,r,g,b,a；alpha 在这里乘上 presentationOpacity。</summary>
    void Vertex(float x, float y, float u, float v, Color c)
    {
        vertices.Add(x);
        vertices.Add(y);
        vertices.Add(u);
        vertices.Add(v);
        vertices.Add(c.R);
        vertices.Add(c.G);
        vertices.Add(c.B);
        vertices.Add(c.A * presentationOpacity);
    }

    /// <summary>两组三角形拼成矩形；角度以度为单位，绕矩形中心旋转，UV 保持输入顺序。</summary>
    public void Quad(Texture texture, Rect r, Color color, Rect? uv = null, float angle = 0, Shader? shader = null)
    {
        if (color.A <= 0 || r.W == 0 || r.H == 0)
        {
            return;
        }
        Select(texture, shader);
        var q = uv ?? new Rect(0, 0, 1, 1);
        var a = new Vector2(r.X, r.Y);
        var b = new Vector2(r.X + r.W, r.Y);
        var c = new Vector2(r.X, r.Y + r.H);
        var d = new Vector2(r.X + r.W, r.Y + r.H);
        if (angle != 0)
        {
            var center = new Vector2(r.X + r.W / 2, r.Y + r.H / 2);
            var m = Matrix3x2.CreateRotation(angle * MathF.PI / 180, center);
            a = Vector2.Transform(a, m);
            b = Vector2.Transform(b, m);
            c = Vector2.Transform(c, m);
            d = Vector2.Transform(d, m);
        }
        Vertex(a.X, a.Y, q.X, q.Y, color);
        Vertex(b.X, b.Y, q.X + q.W, q.Y, color);
        Vertex(c.X, c.Y, q.X, q.Y + q.H, color);
        Vertex(c.X, c.Y, q.X, q.Y + q.H, color);
        Vertex(b.X, b.Y, q.X + q.W, q.Y, color);
        Vertex(d.X, d.Y, q.X + q.W, q.Y + q.H, color);
    }

    /// <summary>提交已变换的四个角，供轨道透视/旋转使用，不再次变换输入坐标。</summary>
    public void Polygon(Texture texture, Vector2 a, Vector2 b, Vector2 c, Vector2 d, Color color, Rect? uv = null, Shader? shader = null)
    {
        var q = uv ?? new Rect(0, 0, 1, 1);
        Select(texture, shader);
        Vertex(a.X, a.Y, q.X, q.Y, color);
        Vertex(b.X, b.Y, q.X + q.W, q.Y, color);
        Vertex(c.X, c.Y, q.X, q.Y + q.H, color);
        Vertex(c.X, c.Y, q.X, q.Y + q.H, color);
        Vertex(b.X, b.Y, q.X + q.W, q.Y, color);
        Vertex(d.X, d.Y, q.X + q.W, q.Y + q.H, color);
    }

    public void Fill(Rect r, Color c) => Quad(White, r, c);

    /// <summary>顶底顶点分别着色；透明度渐变由同一 GPU 插值路径处理。</summary>
    public void QuadGradient(Texture texture, Rect r, Color top, Color bottom, Rect uv)
    {
        if (top.A <= 0 && bottom.A <= 0)
        {
            return;
        }
        Select(texture, null);
        Vertex(r.X, r.Y, uv.X, uv.Y, top);
        Vertex(r.X + r.W, r.Y, uv.X + uv.W, uv.Y, top);
        Vertex(r.X, r.Y + r.H, uv.X, uv.Y + uv.H, bottom);
        Vertex(r.X, r.Y + r.H, uv.X, uv.Y + uv.H, bottom);
        Vertex(r.X + r.W, r.Y, uv.X + uv.W, uv.Y, top);
        Vertex(r.X + r.W, r.Y + r.H, uv.X + uv.W, uv.Y + uv.H, bottom);
    }

    /// <summary>以两端点的中点构造一条水平矩形再旋转到目标角度；thickness 与坐标同为逻辑单位。</summary>
    public void Line(float x1, float y1, float x2, float y2, float thickness, Color color)
    {
        float dx = x2 - x1, dy = y2 - y1, length = MathF.Sqrt(dx * dx + dy * dy);
        Quad(White, new((x1 + x2 - length) / 2, (y1 + y2 - thickness) / 2, length, thickness), color, angle: MathF.Atan2(dy, dx) * 180 / MathF.PI);
    }

    public void Border(Rect r, Color c, float thickness = 1)
    {
        Fill(new(r.X, r.Y, r.W, thickness), c);
        Fill(new(r.X, r.Y + r.H - thickness, r.W, thickness), c);
        Fill(new(r.X, r.Y, thickness, r.H), c);
        Fill(new(r.X + r.W - thickness, r.Y, thickness, r.H), c);
    }
}

