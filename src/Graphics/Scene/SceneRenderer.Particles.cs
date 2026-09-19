using System.Numerics;
using KuroakiGimmick.Core;

namespace KuroakiGimmick.Graphics;

/// <summary>
/// 场景合成协调器。对象背景、资源图层、轨道、固定 HUD 与后处理按显式边界组织；预览和视频导出共用此入口。
/// </summary>
public sealed partial class SceneRenderer
{
    /// <summary>
    /// 绘制烘焙好的确定性粒子。速度按原版 60 Hz 逻辑 tick 定义，与显示帧率无关；
    /// burstsOnly 用于把爆发批次拆到 jacket 之后绘制，两次调用互补而不重复。
    /// </summary>
    void DrawParticles(Session session, double time, bool burstsOnly = false)
    {
        var timeline = session.Timeline;
        double alpha = timeline.Get("particle_alpha", time);
        var now = timeline.ParticleOffset(time);
        for (int i = LowerBound(timeline.Particles, time - 4, p => p.Time); i < timeline.Particles.Count && timeline.Particles[i].Time <= time; i++)
        {
            var p = timeline.Particles[i];
            double age = time - p.Time;
            if (burstsOnly ? !p.Burst : session.NativeGimmick.Data?.BurstAfterJacket == true && p.Burst)
            {
                continue;
            }
            if (time >= p.Death)
            {
                continue;
            }
            if (!p.Burst && alpha <= 0)
            {
                continue;
            }
            var offset = p.Burst && !p.FollowPower? Vector2.Zero : now - timeline.ParticleOffset(p.Time);
            float x = p.X + p.Vx * (float) age * 60 + offset.X, y = p.Y + p.Vy * (float) age * 60 + offset.Y;
            if (x < -50 || x > 370 || y < -50 || y > 230)
            {
                continue;
            }
            // pt_diamonddust 是 9×9、四帧、原点 (4,4) 的精灵。
            // 保留其透明中心与逐帧菱形描边，不用实心方块近似。
            canvas.Quad(dustTextures[p.Frame], new(x - 4, y - 4, 9, 9), Color.Hsv(p.Hue,
                p.Saturation).Alpha((p.Burst? 1 : alpha) * (1 - age / p.Life)));
        }
    }

    /// <summary>slash_anycol 斜线；col_convertion 为 0 时按原版交换 R 与 B，不是颜色解析写反。</summary>
    void DrawSlashes(Session session, double time)
    {
        var timeline = session.Timeline;
        for (int i = LowerBound(timeline.Callbacks, time - 1, e => e.Time); i < timeline.Callbacks.Count && timeline.Callbacks[i].Time <= time; i++)
        {
            var e = timeline.Callbacks[i];
            if (e.Name != "slash_anycol")
            {
                continue;
            }
            uint rgb = (uint) Math.Clamp(timeline.Get("set_slash_col", e.Time), 0, 16777215);
            if (timeline.Get("col_convertion", e.Time) == 0)
            {
                rgb = ((rgb & 255) << 16) | (rgb & 0xFF00) | ((rgb >> 16) & 255);
            }
            uint seed = (uint) e.Index * 17 + 623;
            float age = (float)(time - e.Time), width = 12 * (1 - age) * (1 - age);
            if (rgb != 0)
            {
                canvas.Blend(BlendFactor.InverseDestinationColor, BlendFactor.Zero);
            }
            // 反相目标色、目标系数取零；无论上面是否切换过，结束时都要恢复常规混合。
            canvas.Line(-6, Timeline.Hash(seed) * 180, 326, Timeline.Hash(seed + 1) * 180, width, Color.Hex(rgb));
            canvas.Blend(BlendFactor.SourceAlpha, BlendFactor.InverseSourceAlpha);
        }
    }
}

