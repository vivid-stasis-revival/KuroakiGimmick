using System.Numerics;
using KuroakiGimmick.Core;

namespace KuroakiGimmick.Graphics;

public sealed partial class NativeGimmickRenderer
{
    /// <summary>按精灵名取一帧纹理，走与对象绘制同一条缓存；调用方不得持有或释放返回的纹理。</summary>
    public Texture? SequenceTexture(Session session, string sprite, int frame = 0)
    { Prepare(session); return Image(sprite, frame); }

    /// <summary>
    /// 单张精灵的绘制辅助。filtered 为真时借用 post mode 0 的 shader 及其 uniform/sampler；
    /// uniform 取值非有限或超出 float 范围、以及任一 sampler 图片缺失，都直接放弃本次绘制并写诊断，不退化成无 shader 的版本。
    /// sampler 单元从 1 开始，0 留给 gm_BaseTexture。原点取自 profile 的精灵定义，不假定居中。
    /// </summary>
    public void DrawSequenceSprite(Session session, string name, int frame, double x, double y, double sx, double sy, double alpha, double time, bool filtered = false)
    {
        Prepare(session);
        if (current == null || !current.Sprites.TryGetValue(name, out var sprite) || alpha <= 0 || sx == 0 || sy == 0 || Image(name, frame) is not { } texture) return;
        Shader? program = null;
        if (filtered && current.Data?.PostModes.TryGetValue(0, out var mode) == true && shaders.TryGetValue(mode.Shader, out program))
        {
            canvas.Flush();
            foreach (var pair in mode.Uniforms)
            {
                double[] v = pair.Value.Select(s => s.Value(session.Timeline, time)).ToArray();
                if (v.Any(n => !double.IsFinite(n) || Math.Abs(n) > float.MaxValue)) { current.Fail(name, "Invalid sequence shader value."); return; }
                switch (v.Length)
                {
                    case 1: program.Float(pair.Key, v[0]); break;
                    case 2: program.Vec2(pair.Key, v[0], v[1]); break;
                    case 3: program.Vec3(pair.Key, v[0], v[1], v[2]); break;
                    case 4: program.Vec4(pair.Key, new((float)v[0], (float)v[1], (float)v[2], (float)v[3])); break;
                }
            }
            int unit = 1;
            foreach (var sampler in mode.Samplers)
            {
                if (Image(sampler.Value, repeat: true, linear: mode.LinearSamplers.Contains(sampler.Key)) is not { } image) return;
                canvas.Gpu.BindTexture(unit, image); program.Int(sampler.Key, unit++);
            }
        }
        var matrix = Matrix3x2.CreateTranslation(-sprite.OriginX, -sprite.OriginY) * Matrix3x2.CreateScale((float)sx, (float)sy) * Matrix3x2.CreateTranslation((float)x, (float)y);
        canvas.Polygon(texture, Vector2.Transform(Vector2.Zero, matrix), Vector2.Transform(new(sprite.Width, 0), matrix),
            Vector2.Transform(new(0, sprite.Height), matrix), Vector2.Transform(new(sprite.Width, sprite.Height), matrix), Color.White.Alpha(alpha), shader: program);
        canvas.Flush();
    }
}
