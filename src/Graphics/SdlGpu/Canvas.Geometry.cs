// 二维几何到固定 8-float 顶点流的转换，不读取谱面身份或对象类型。

using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using KuroakiGimmick.Native;

namespace KuroakiGimmick.Graphics;

public sealed partial class Canvas : IDisposable
{
#if KG_SWITCH
    internal bool DebugNextVertex;
#endif
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
#if KG_SWITCH
        if (DebugNextVertex)
        {
            DebugNextVertex = false;
            int start = vertices.Count - 4;
            Console.Error.WriteLine($"[K/G text vertex] RGBA={vertices[start]:F6},{vertices[start + 1]:F6},{vertices[start + 2]:F6},{vertices[start + 3]:F6}");
        }
#endif
    }

    /// <summary>两组三角形拼成矩形；角度以度为单位，绕矩形中心旋转，UV 保持输入顺序。</summary>
    /// <param name="texture">用于该操作的 GPU 纹理。</param>
    /// <param name="r">目标矩形。</param>
    /// <param name="color">绘制使用的 RGBA 颜色。</param>
    /// <param name="uv">源纹理的归一化采样矩形。</param>
    /// <param name="angle">旋转角。</param>
    /// <param name="shader">本次绘制使用的 shader。</param>
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
        if (vertices.Count >= 48 * 8192) Flush();
    }

    /// <summary>提交已变换的四个角，供轨道透视/旋转使用，不再次变换输入坐标。</summary>
    /// <param name="texture">用于该操作的 GPU 纹理。</param>
    /// <param name="a">四边形的第一个顶点。</param>
    /// <param name="b">第二个顶点。</param>
    /// <param name="c">第三个顶点。</param>
    /// <param name="d">第四个顶点。</param>
    /// <param name="color">绘制使用的 RGBA 颜色。</param>
    /// <param name="uv">源纹理的归一化采样矩形。</param>
    /// <param name="shader">本次绘制使用的 shader。</param>
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
        if (vertices.Count >= 48 * 8192) Flush();
    }

    /// <summary>用指定颜色填充矩形区域。</summary>
    /// <param name="r">填充的矩形。</param>
    /// <param name="c">填充颜色。</param>
    public void Fill(Rect r, Color c) => Quad(White, r, c);

    /// <summary>顶底顶点分别着色；透明度渐变由同一 GPU 插值路径处理。</summary>
    /// <param name="texture">用于该操作的 GPU 纹理。</param>
    /// <param name="r">目标矩形。</param>
    /// <param name="top">顶部颜色。</param>
    /// <param name="bottom">底部颜色。</param>
    /// <param name="uv">源纹理的归一化采样矩形。</param>
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
        if (vertices.Count >= 48 * 8192) Flush();
    }

    /// <summary>以两端点的中点构造一条水平矩形再旋转到目标角度；thickness 与坐标同为逻辑单位。</summary>
    /// <param name="x1">起点横坐标。</param>
    /// <param name="y1">起点纵坐标。</param>
    /// <param name="x2">终点横坐标。</param>
    /// <param name="y2">终点纵坐标。</param>
    /// <param name="thickness">线条或边框的逻辑宽度。</param>
    /// <param name="color">绘制使用的 RGBA 颜色。</param>
    public void Line(float x1, float y1, float x2, float y2, float thickness, Color color)
    {
        float dx = x2 - x1, dy = y2 - y1, length = MathF.Sqrt(dx * dx + dy * dy);
        Quad(White, new((x1 + x2 - length) / 2, (y1 + y2 - thickness) / 2, length, thickness), color, angle: MathF.Atan2(dy, dx) * 180 / MathF.PI);
    }

    /// <summary>在指定矩形边缘绘制边框。</summary>
    /// <param name="r">要描边的矩形。</param>
    /// <param name="c">边框颜色。</param>
    /// <param name="thickness">线条或边框的逻辑宽度。</param>
    public void Border(Rect r, Color c, float thickness = 1)
    {
        Fill(new(r.X, r.Y, r.W, thickness), c);
        Fill(new(r.X, r.Y + r.H - thickness, r.W, thickness), c);
        Fill(new(r.X, r.Y, thickness, r.H), c);
        Fill(new(r.X + r.W - thickness, r.Y, thickness, r.H), c);
    }
}

