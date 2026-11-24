// 统一计算 proxy 的源区域、变换、透视裁剪与遮罩采样。

using System.Numerics;
using KuroakiGimmick.Core;

namespace KuroakiGimmick.Graphics;

/// <summary>场景与编辑器参考线共用的 proxy 姿态，源裁剪边界保留方向。</summary>
public readonly record struct ProxyGeometry
{
    /// <summary>源裁剪区域的横向起边，可与右边界反向。</summary>
    public float Left { get; private init; }
    /// <summary>源裁剪区域的横向终边。</summary>
    public float Right { get; private init; }
    /// <summary>源裁剪区域的纵向起边。</summary>
    public float Top { get; private init; }
    /// <summary>源裁剪区域的纵向终边。</summary>
    public float Bottom { get; private init; }
    /// <summary>proxy 的采样不透明度。</summary>
    public double Alpha { get; private init; }
    /// <summary>当前时刻的横向位置。</summary>
    public double X { get; private init; }
    /// <summary>当前时刻的纵向位置。</summary>
    public double Y { get; private init; }
    /// <summary>当前时刻的平面旋转角。</summary>
    public float Angle { get; private init; }
    /// <summary>水平缩放倍率。</summary>
    public double ScaleX { get; private init; }
    /// <summary>垂直缩放倍率。</summary>
    public double ScaleY { get; private init; }
    /// <summary>变换使用的局部原点。</summary>
    public Vector2 Origin { get; private init; }
    /// <summary>当前 proxy 是否采用透视投影。</summary>
    public bool Projective { get; private init; }
    /// <summary>当前几何状态是否满足绘制条件。</summary>
    public bool Renderable { get; private init; }
    /// <summary>平面仿射变换矩阵。</summary>
    public Matrix3x2 Affine { get; private init; }
    /// <summary>将源点变换到齐次空间的投影矩阵。</summary>
    public Matrix4x4 Projection { get; private init; }
    /// <summary>将两端取最小值、尺寸取绝对值后的源裁剪矩形。</summary>
    public Rect Source => new(Math.Min(Left, Right), Math.Min(Top, Bottom), Math.Abs(Right - Left), Math.Abs(Bottom - Top));
    /// <summary>保留翻转方向的归一化源纹理区域，以 320×180 为基准。</summary>
    public Rect UV => new(Left / 320, Top / 180, (Right - Left) / 320, (Bottom - Top) / 180);

    /// <summary>参考线与拾取共用的轨道区域，排除宽裁剪中额外露出的背景和界面。</summary>
    /// <param name="waveX">横向波形参考偏移。</param>
    /// <param name="waveY">纵向波形参考偏移。</param>
    /// <returns>用于轨道标记与拾取的源区域。</returns>
    public Rect TrackRegion(double waveX = 0, double waveY = 0)
    {
        var s = Source;
        float left = Math.Max(s.X, 113 - (float)Math.Abs(waveX));
        float right = Math.Min(s.X + s.W, 208 + (float)Math.Abs(waveX));
        float top = Math.Max(s.Y, -(float)Math.Abs(waveY));
        float bottom = Math.Min(s.Y + s.H, 165 + (float)Math.Abs(waveY));
        return new(left, top, Math.Max(0, right - left), Math.Max(0, bottom - top));
    }

    /// <summary>从指定 proxy 的轨道与默认规则采样当前几何状态。</summary>
    /// <param name="timeline">用于采样轨道和回调的时间轴。</param>
    /// <param name="time">采样时刻，单位为秒。</param>
    /// <param name="target">目标 proxy 的编号。</param>
    /// <param name="custom">是否使用自定义对象的 proxy 规则。</param>
    /// <returns>该 proxy 的裁剪、变换及投影状态。</returns>
    public static ProxyGeometry Sample(Timeline timeline, double time, int target, bool custom)
    {
        bool mode00 = timeline.Native?.Data?.ProxyMode00 == true;
        double M(string name) => timeline.Get(name, time, target);
        double scale = M("przm") * M("przmb") * M("przmc");
        double sx = scale * M("przx") * Math.Cos(M("prrx") * Math.PI / 180);
        double sy = scale * M("przy") * Math.Cos(M("prry") * Math.PI / 180);
        double x = M("prx") + M("prxb") + M("prxc") + M("prxd");
        double y = M("pry") + M("pryb") + M("pryc") + M("pryd");
        float angle = (float)((M("prrz") + M("prrzb")) * timeline.Get("rotdir", time));
        if (mode00) angle = (float)(M("prrz") + M("prrzb"));
        float left = 113 - (float)M("shxa"), right = 208 + (float)M("shxa"), top = 0, bottom = 165;
        if (!mode00 && (!ProxyCropDefaults.Matches(M("prct"), .08) || M("prcb") != 0))
        {
            top = (float)(180 * M("prcb") - 1);
            bottom = (float)(180 - Math.Ceiling(180 * M("prct")));
        }
        if (!mode00 && (!ProxyCropDefaults.Matches(M("prcl"), .35) || !ProxyCropDefaults.Matches(M("prcr"), .35)))
        {
            left = (float)M("prcl");
            right = (float)M("prcr");
        }
        var origin = new Vector2(custom ? 160 - (float)M("shxa") : 160, 82);
        var affine = Matrix3x2.CreateTranslation(-origin) * Matrix3x2.CreateScale((float)sx, (float)sy) *
            Matrix3x2.CreateRotation((custom ? angle : -angle) * MathF.PI / 180) *
            new Matrix3x2(1, -(float)M("prsy"), -(float)M("prsx"), 1, 0, 0) *
            Matrix3x2.CreateTranslation(160 + (float)x, 82 + (float)y);
        if (mode00)
        {
            origin = new((left + right) / 2, 82.5f);
            affine = Matrix3x2.CreateTranslation(-origin) * Matrix3x2.CreateScale((float)sx, (float)sy)
                * Matrix3x2.CreateRotation(-angle * MathF.PI / 180) * Matrix3x2.CreateTranslation(160 + (float)x, 82 + (float)y)
                * new Matrix3x2(1, 0, (float)M("prsx"), 1, 0, 0);
        }
        bool projective = custom && (M("prtrX") != 0 || M("prtrY") != 0);
        var projection = new Matrix4x4(affine);
        if (projective)
        {
            // 按行向量复现原顶点 shader：先旋转、构造 W、斜切，再加上平移量乘 W。GPU 光栅化和 UV 插值均保留 W。
            float c = (float)Math.Cos(angle * Math.PI / 180), s = (float)Math.Sin(angle * Math.PI / 180);
            var rotation = new Matrix4x4(c, s, 0, 0, -s, c, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1);
            var trapezoid = Matrix4x4.Identity;
            trapezoid.M14 = (float)M("prtrX");
            trapezoid.M24 = (float)M("prtrY");
            var skew = Matrix4x4.Identity;
            skew.M12 = -(float)M("prsy");
            skew.M21 = -(float)M("prsx");
            projection = Matrix4x4.CreateTranslation(-origin.X, -origin.Y, 0) *
                Matrix4x4.CreateScale((float)sx, (float)sy, 1) * rotation * trapezoid * skew *
                Matrix4x4.CreateTranslation((float)(160 + x), (float)(82 + y), 0);
        }
        bool finite = float.IsFinite(left) && float.IsFinite(right) && float.IsFinite(top) && float.IsFinite(bottom) &&
            float.IsFinite(projection.M11) && float.IsFinite(projection.M12) && float.IsFinite(projection.M14) &&
            float.IsFinite(projection.M21) && float.IsFinite(projection.M22) && float.IsFinite(projection.M24) &&
            float.IsFinite(projection.M41) && float.IsFinite(projection.M42) && float.IsFinite(projection.M44);
        return new()
        {
            Left = left,
            Right = right,
            Top = top,
            Bottom = bottom,
            Alpha = M("pra"),
            X = x,
            Y = y,
            Angle = angle,
            ScaleX = sx,
            ScaleY = sy,
            Origin = origin,
            Affine = affine,
            Projection = projection,
            Projective = projective,
            Renderable = finite && double.IsFinite(sx) && double.IsFinite(sy) &&
                (mode00 ? M("pra") > .001 && Math.Abs(sx) > .001 && Math.Abs(sy) > .001 && Math.Abs(x) < 240 && Math.Abs(y - 8) < 240
                    : custom || Math.Abs(sx) <= 50 && Math.Abs(sy) <= 50)
        };
    }

    internal void ApplyProjection(Shader shader)
    {
        var m = Projection;
        shader.Vec4("proxyRowX", new(m.M11, m.M21, m.M31, m.M41));
        shader.Vec4("proxyRowY", new(m.M12, m.M22, m.M32, m.M42));
        shader.Vec4("proxyRowW", new(m.M14, m.M24, m.M34, m.M44));
    }

    /// <summary>计算尚未按 W 分量归一化的投影坐标。</summary>
    /// <param name="source">源画布上的二维点。</param>
    /// <returns>透视除法前的齐次坐标。</returns>
    public Vector4 Homogeneous(Vector2 source) => Vector4.Transform(new Vector4(source, 0, 1), Projection);
    /// <summary>将源点投影到画布，检查 W 分量和输出坐标的有效性。</summary>
    /// <param name="source">源画布上的点。</param>
    /// <param name="point">输出的画布投影坐标。</param>
    /// <returns>投影分量有限且 W 有效时为 true。</returns>
    public bool TryProject(Vector2 source, out Vector2 point)
    {
        var p = Homogeneous(source);
        point = new(p.X / p.W, p.Y / p.W);
        return p.W > .00001f && float.IsFinite(point.X) && float.IsFinite(point.Y);
    }
    /// <summary>反转投影矩阵以恢复源坐标，不可逆或投影无效时返回 false。</summary>
    /// <param name="point">画布中的投影点。</param>
    /// <param name="source">输出恢复的源坐标。</param>
    /// <returns>矩阵可逆且恢复坐标有效时为 true。</returns>
    public bool TryUnproject(Vector2 point, out Vector2 source)
    {
        source = default;
        if (!Matrix4x4.Invert(Projection, out var inverse)) return false;
        var p = Vector4.Transform(new Vector4(point, 0, 1), inverse);
        source = new(p.X / p.W, p.Y / p.W);
        return p.W > .00001f && float.IsFinite(source.X) && float.IsFinite(source.Y);
    }

    /// <summary>先裁剪再除以 W，避免跨过相机平面的透视边缘产生无穷坐标。</summary>
    /// <param name="sourceA">源线段起点。</param>
    /// <param name="sourceB">源线段终点。</param>
    /// <param name="a">输出裁剪后的起点。</param>
    /// <param name="b">输出裁剪后的终点。</param>
    /// <returns>裁剪后仍有有效可见线段时为 true。</returns>
    public bool ClipSegment(Vector2 sourceA, Vector2 sourceB, out Vector2 a, out Vector2 b)
    {
        var p = Homogeneous(sourceA);
        var q = Homogeneous(sourceB);
        a = b = default;
        if (!Renderable) return false;
        for (int plane = 0; plane < 5; plane++)
        {
            float Distance(Vector4 v) => plane switch
            { 0 => v.W - .00001f, 1 => v.X, 2 => 320 * v.W - v.X, 3 => v.Y, _ => 180 * v.W - v.Y };
            float dp = Distance(p), dq = Distance(q);
            if (dp < 0 && dq < 0) return false;
            if (dp < 0 || dq < 0)
            {
                var intersection = Vector4.Lerp(p, q, dp / (dp - dq));
                if (dp < 0) p = intersection;
                else q = intersection;
            }
        }
        a = new(p.X / p.W, p.Y / p.W);
        b = new(q.X / q.W, q.Y / q.W);
        return float.IsFinite(a.X) && float.IsFinite(a.Y) && float.IsFinite(b.X) && float.IsFinite(b.Y);
    }

    /// <summary>在齐次空间裁剪源区域的可见多边形，再进行透视除法。矩形依次裁剪到视口和相机平面后，九个顶点足以容纳结果。</summary>
    /// <param name="source">源画布上的矩形。</param>
    /// <param name="output">接收裁剪后多边形顶点的缓冲区。</param>
    /// <returns>有效可见顶点数，无可见区域时返回 0。</returns>
    /// <exception cref="ArgumentException">输出缓冲区无法容纳九个顶点。</exception>
    public int ClipRegion(Rect source, Span<Vector2> output)
    {
        if (output.Length < 9) throw new ArgumentException("Proxy clip output needs nine vertices.", nameof(output));
        if (!Renderable || source.W <= 0 || source.H <= 0 ||
            !float.IsFinite(source.X + source.W) || !float.IsFinite(source.Y + source.H)) return 0;
        Span<Vector4> points = stackalloc Vector4[12], scratch = stackalloc Vector4[12];
        points[0] = Homogeneous(new(source.X, source.Y));
        points[1] = Homogeneous(new(source.X + source.W, source.Y));
        points[2] = Homogeneous(new(source.X + source.W, source.Y + source.H));
        points[3] = Homogeneous(new(source.X, source.Y + source.H));
        int count = 4;
        for (int plane = 0; plane < 5 && count > 0; plane++)
        {
            float Distance(Vector4 v) => plane switch
            { 0 => v.W - .00001f, 1 => v.X, 2 => 320 * v.W - v.X, 3 => v.Y, _ => 180 * v.W - v.Y };
            int nextCount = 0;
            var previous = points[count - 1];
            float previousDistance = Distance(previous);
            for (int i = 0; i < count; i++)
            {
                var current = points[i];
                float distance = Distance(current);
                if (previousDistance >= 0 != (distance >= 0))
                    scratch[nextCount++] = Vector4.Lerp(previous, current, previousDistance / (previousDistance - distance));
                if (distance >= 0) scratch[nextCount++] = current;
                previous = current;
                previousDistance = distance;
            }
            var swap = points;
            points = scratch;
            scratch = swap;
            count = nextCount;
        }
        float area = 0;
        for (int i = 0; i < count; i++)
        {
            output[i] = new(points[i].X / points[i].W, points[i].Y / points[i].W);
            if (!float.IsFinite(output[i].X) || !float.IsFinite(output[i].Y)) return 0;
        }
        for (int i = 0; i < count; i++)
        {
            var a = output[i];
            var b = output[(i + 1) % count];
            area += a.X * b.Y - a.Y * b.X;
        }
        return Math.Abs(area) > .0001f ? count : 0;
    }
}

/// <summary>遮罩使用整块画布的 UV，独立于有方向的 proxy 源裁剪框。</summary>
/// <param name="Left">左侧裁剪比例。</param>
/// <param name="Right">右侧裁剪比例。</param>
/// <param name="Top">上侧裁剪比例。</param>
/// <param name="Bottom">下侧裁剪比例。</param>
/// <param name="FadeLeft">左边缘淡化宽度。</param>
/// <param name="FadeRight">右边缘淡化宽度。</param>
/// <param name="FadeTop">上边缘淡化宽度。</param>
/// <param name="FadeBottom">下边缘淡化宽度。</param>
public readonly record struct ProxyMask(float Left, float Right, float Top, float Bottom,
    float FadeLeft, float FadeRight, float FadeTop, float FadeBottom)
{
    /// <summary>从目标 proxy 的裁剪和淡化轨道采样遮罩。</summary>
    /// <param name="timeline">用于采样轨道和回调的时间轴。</param>
    /// <param name="time">采样时刻，单位为秒。</param>
    /// <param name="target">目标 proxy 的编号。</param>
    /// <returns>该时刻的四边裁剪与渐隐状态。</returns>
    public static ProxyMask Sample(Timeline timeline, double time, int target)
    {
        float M(string name) => (float)timeline.Get(name, time, target);
        return new(M("shcl"), M("shcr"), M("shct"), M("shcb"), M("shfl"), M("shfr"), M("shft"), M("shfb"));
    }
    /// <summary>按源点到四条裁剪边的距离计算合成后的遮罩不透明度。</summary>
    /// <param name="source">完整场地中的逻辑点。</param>
    /// <returns>四边渐隐相乘后的不透明度。</returns>
    public float AlphaAt(Vector2 source)
    {
        static float Ramp(float distance, float fade) => Math.Clamp(distance / Math.Max(fade, .0001f), 0, 1);
        return Ramp(source.X / 320 - Left, FadeLeft) * Ramp(1 - Right - source.X / 320, FadeRight) *
            Ramp(source.Y / 180 - Top, FadeTop) * Ramp(1 - Bottom - source.Y / 180, FadeBottom);
    }
    internal void Apply(Shader shader)
    {
        shader.Float("cl", Left);
        shader.Float("cr", Right);
        shader.Float("ct", Top);
        shader.Float("cb", Bottom);
        shader.Float("fl", FadeLeft);
        shader.Float("fr", FadeRight);
        shader.Float("ft", FadeTop);
        shader.Float("fb", FadeBottom);
    }
}
