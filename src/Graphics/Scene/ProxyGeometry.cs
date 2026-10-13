using System.Numerics;
using KuroakiGimmick.Core;

namespace KuroakiGimmick.Graphics;

/// <summary>One evaluated proxy pose, shared by scene rendering and editor guides. Source bounds stay signed.</summary>
public readonly record struct ProxyGeometry
{
    public float Left { get; private init; }
    public float Right { get; private init; }
    public float Top { get; private init; }
    public float Bottom { get; private init; }
    public double Alpha { get; private init; }
    public double X { get; private init; }
    public double Y { get; private init; }
    public float Angle { get; private init; }
    public double ScaleX { get; private init; }
    public double ScaleY { get; private init; }
    public Vector2 Origin { get; private init; }
    public bool Projective { get; private init; }
    public bool Renderable { get; private init; }
    public Matrix3x2 Affine { get; private init; }
    public Matrix4x4 Projection { get; private init; }
    public Rect Source => new(Math.Min(Left, Right), Math.Min(Top, Bottom), Math.Abs(Right - Left), Math.Abs(Bottom - Top));
    public Rect UV => new(Left / 320, Top / 180, (Right - Left) / 320, (Bottom - Top) / 180);

    /// <summary>Lane reference shared by guide rendering and picking, excluding extra background/UI in a wide crop.</summary>
    public Rect TrackRegion(double waveX = 0, double waveY = 0)
    {
        var s = Source;
        float left = Math.Max(s.X, 113 - (float)Math.Abs(waveX));
        float right = Math.Min(s.X + s.W, 208 + (float)Math.Abs(waveX));
        float top = Math.Max(s.Y, -(float)Math.Abs(waveY));
        float bottom = Math.Min(s.Y + s.H, 165 + (float)Math.Abs(waveY));
        return new(left, top, Math.Max(0, right - left), Math.Max(0, bottom - top));
    }

    public static ProxyGeometry Sample(Timeline timeline, double time, int target, bool custom)
    {
        double M(string name) => timeline.Get(name, time, target);
        double scale = M("przm") * M("przmb") * M("przmc");
        double sx = scale * M("przx") * Math.Cos(M("prrx") * Math.PI / 180);
        double sy = scale * M("przy") * Math.Cos(M("prry") * Math.PI / 180);
        double x = M("prx") + M("prxb") + M("prxc") + M("prxd");
        double y = M("pry") + M("pryb") + M("pryc") + M("pryd");
        float angle = (float)((M("prrz") + M("prrzb")) * timeline.Get("rotdir", time));
        float left = 113 - (float)M("shxa"), right = 208 + (float)M("shxa"), top = 0, bottom = 165;
        if (M("prct") != .08 || M("prcb") != 0)
        { top = (float)(180 * M("prcb") - 1); bottom = (float)(180 - Math.Ceiling(180 * M("prct"))); }
        if (M("prcl") != .35 || M("prcr") != .35)
        { left = (float)M("prcl"); right = (float)M("prcr"); }
        var origin = new Vector2(custom ? 160 - (float)M("shxa") : 160, 82);
        var affine = Matrix3x2.CreateTranslation(-origin) * Matrix3x2.CreateScale((float)sx, (float)sy) *
            Matrix3x2.CreateRotation((custom ? angle : -angle) * MathF.PI / 180) *
            new Matrix3x2(1, -(float)M("prsy"), -(float)M("prsx"), 1, 0, 0) *
            Matrix3x2.CreateTranslation(160 + (float)x, 82 + (float)y);
        bool projective = custom && (M("prtrX") != 0 || M("prtrY") != 0);
        var projection = new Matrix4x4(affine);
        if (projective)
        {
            // Row-vector equivalent of the original vertex shader: rotate, build W, skew,
            // then add translation * W. Keep W through GPU rasterization (including UVs).
            float c = (float)Math.Cos(angle * Math.PI / 180), s = (float)Math.Sin(angle * Math.PI / 180);
            var rotation = new Matrix4x4(c, s, 0, 0, -s, c, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1);
            var trapezoid = Matrix4x4.Identity;
            trapezoid.M14 = (float)M("prtrX"); trapezoid.M24 = (float)M("prtrY");
            var skew = Matrix4x4.Identity;
            skew.M12 = -(float)M("prsy"); skew.M21 = -(float)M("prsx");
            projection = Matrix4x4.CreateTranslation(-origin.X, -origin.Y, 0) *
                Matrix4x4.CreateScale((float)sx, (float)sy, 1) * rotation * trapezoid * skew *
                Matrix4x4.CreateTranslation((float)(160 + x), (float)(82 + y), 0);
        }
        bool finite = float.IsFinite(left) && float.IsFinite(right) && float.IsFinite(top) && float.IsFinite(bottom) &&
            float.IsFinite(projection.M11) && float.IsFinite(projection.M12) && float.IsFinite(projection.M14) &&
            float.IsFinite(projection.M21) && float.IsFinite(projection.M22) && float.IsFinite(projection.M24) &&
            float.IsFinite(projection.M41) && float.IsFinite(projection.M42) && float.IsFinite(projection.M44);
        return new() { Left = left, Right = right, Top = top, Bottom = bottom, Alpha = M("pra"),
            X = x, Y = y, Angle = angle, ScaleX = sx, ScaleY = sy, Origin = origin,
            Affine = affine, Projection = projection, Projective = projective,
            Renderable = finite && double.IsFinite(sx) && double.IsFinite(sy) &&
                (custom || Math.Abs(sx) <= 50 && Math.Abs(sy) <= 50) };
    }

    internal void ApplyProjection(Shader shader)
    {
        var m = Projection;
        shader.Vec4("proxyRowX", new(m.M11, m.M21, m.M31, m.M41));
        shader.Vec4("proxyRowY", new(m.M12, m.M22, m.M32, m.M42));
        shader.Vec4("proxyRowW", new(m.M14, m.M24, m.M34, m.M44));
    }

    public Vector4 Homogeneous(Vector2 source) => Vector4.Transform(new Vector4(source, 0, 1), Projection);
    public bool TryProject(Vector2 source, out Vector2 point)
    {
        var p = Homogeneous(source);
        point = new(p.X / p.W, p.Y / p.W);
        return p.W > .00001f && float.IsFinite(point.X) && float.IsFinite(point.Y);
    }
    public bool TryUnproject(Vector2 point, out Vector2 source)
    {
        source = default;
        if (!Matrix4x4.Invert(Projection, out var inverse)) return false;
        var p = Vector4.Transform(new Vector4(point, 0, 1), inverse);
        source = new(p.X / p.W, p.Y / p.W);
        return p.W > .00001f && float.IsFinite(source.X) && float.IsFinite(source.Y);
    }

    /// <summary>Clip BEFORE dividing by W, so perspective edges crossing the camera plane stay finite.</summary>
    public bool ClipSegment(Vector2 sourceA, Vector2 sourceB, out Vector2 a, out Vector2 b)
    {
        var p = Homogeneous(sourceA); var q = Homogeneous(sourceB);
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
                if (dp < 0) p = intersection; else q = intersection;
            }
        }
        a = new(p.X / p.W, p.Y / p.W); b = new(q.X / q.W, q.Y / q.W);
        return float.IsFinite(a.X) && float.IsFinite(a.Y) && float.IsFinite(b.X) && float.IsFinite(b.Y);
    }

    /// <summary>Visible polygon of a source region, clipped in homogeneous space before division.
    /// Nine vertices suffice for a rectangle clipped by the viewport and camera planes.</summary>
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
            var previous = points[count - 1]; float previousDistance = Distance(previous);
            for (int i = 0; i < count; i++)
            {
                var current = points[i]; float distance = Distance(current);
                if (previousDistance >= 0 != (distance >= 0))
                    scratch[nextCount++] = Vector4.Lerp(previous, current, previousDistance / (previousDistance - distance));
                if (distance >= 0) scratch[nextCount++] = current;
                previous = current; previousDistance = distance;
            }
            var swap = points; points = scratch; scratch = swap; count = nextCount;
        }
        float area = 0;
        for (int i = 0; i < count; i++)
        {
            output[i] = new(points[i].X / points[i].W, points[i].Y / points[i].W);
            if (!float.IsFinite(output[i].X) || !float.IsFinite(output[i].Y)) return 0;
        }
        for (int i = 0; i < count; i++)
        {
            var a = output[i]; var b = output[(i + 1) % count];
            area += a.X * b.Y - a.Y * b.X;
        }
        return Math.Abs(area) > .0001f ? count : 0;
    }
}

/// <summary>Mask coordinates are full-field UVs, independent of the signed proxy source crop.</summary>
public readonly record struct ProxyMask(float Left, float Right, float Top, float Bottom,
    float FadeLeft, float FadeRight, float FadeTop, float FadeBottom)
{
    public static ProxyMask Sample(Timeline timeline, double time, int target)
    {
        float M(string name) => (float)timeline.Get(name, time, target);
        return new(M("shcl"), M("shcr"), M("shct"), M("shcb"), M("shfl"), M("shfr"), M("shft"), M("shfb"));
    }
    public float AlphaAt(Vector2 source)
    {
        static float Ramp(float distance, float fade) => Math.Clamp(distance / Math.Max(fade, .0001f), 0, 1);
        return Ramp(source.X / 320 - Left, FadeLeft) * Ramp(1 - Right - source.X / 320, FadeRight) *
            Ramp(source.Y / 180 - Top, FadeTop) * Ramp(1 - Bottom - source.Y / 180, FadeBottom);
    }
    internal void Apply(Shader shader)
    {
        shader.Float("cl", Left); shader.Float("cr", Right); shader.Float("ct", Top); shader.Float("cb", Bottom);
        shader.Float("fl", FadeLeft); shader.Float("fr", FadeRight); shader.Float("ft", FadeTop); shader.Float("fb", FadeBottom);
    }
}
