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

    void DrawLoreleiSlashes(Session session, double time)
    {
        var slashes = session.Timeline.LoreleiSlashes;
        int first = LowerBound(slashes, time - 1, e => e.Time);
        if (first >= slashes.Count || slashes[first].Time > time) return;
        for (int pass = 0; pass < 2; pass++)
        {
            canvas.Blend(pass == 0 ? BlendFactor.SourceAlpha : BlendFactor.InverseDestinationColor,
                pass == 0 ? BlendFactor.InverseSourceAlpha : BlendFactor.Zero);
            for (int i = first; i < slashes.Count && slashes[i].Time <= time; i++)
            {
                var e = slashes[i];
                double age = time - e.Time;
                if (age >= 1 || (e.Color == 0) != (pass == 0)) continue;
                var color = Color.GameMaker(e.Color);
                float width = (float)(12 * (1 - age) * (1 - age));
                for (int n = 0; n < e.Count; n++)
                {
                    uint seed = unchecked((uint)e.Index * 65537 + (uint)n * 127 + 3127);
                    float side = Timeline.Hash(seed) < .5f ? 0 : 207;
                    canvas.Line(side + Timeline.Hash(seed + 1) * 110, -10,
                        side + Timeline.Hash(seed + 2) * 110, 190, width, color);
                }
            }
        }
        canvas.Blend(BlendFactor.SourceAlpha, BlendFactor.InverseSourceAlpha);
    }

    /// <summary>slash_anycol 斜线；非零 duration 会在区间内按 60 Hz 每 tick 生成新实例。col_convertion=0 时按原版交换 R/B。</summary>
    void DrawSlashes(Session session, double time)
    {
        var timeline = session.Timeline;
        slashInstanceCache.Clear();

        // 零时长事件保持原来的单次 callback 与种子，避免已有谱面的静态画面发生无谓变化。
        for (int i = LowerBound(timeline.Callbacks, time - 1, e => e.Time); i < timeline.Callbacks.Count && timeline.Callbacks[i].Time <= time; i++)
        {
            var e = timeline.Callbacks[i];
            if (e.Name == "slash_anycol")
                slashInstanceCache.Add((e.Time, (uint)e.Index * 17 + 623, e.Index));
        }

        // 非零 duration 不预烘焙 Callback。每帧只展开最近 1 秒内仍存活的生成 tick，
        // 因此超长 slash 区间不会把内存按 duration 线性炸开，seek/倒拖也仍完全确定（#33）。
        foreach (var span in timeline.SlashSpans)
        {
            if (span.Start > time) break;
            if (span.End <= time - 1) continue;

            double visibleStart = Math.Max(span.Start, time - 1);
            int firstTick = Math.Max(0, (int)Math.Ceiling((visibleStart - span.Start) * 60));
            int lastTick = Math.Min(span.TickCount - 1, (int)Math.Floor((time - span.Start) * 60));
            for (int tick = firstTick; tick <= lastTick; tick++)
            {
                double spawnTime = span.Start + tick / 60.0;
                // 原 callback 序号与 tick 一起构成种子；连续 slash 不会全部叠成同一条线。
                uint seed = unchecked((uint)span.Index * 17u + (uint)tick * 65537u + 623u);
                slashInstanceCache.Add((spawnTime, seed, span.Index));
            }
        }

        // 多个持续区间重叠时仍按实际出生时间绘制；同刻再按源顺序稳定排序。
        slashInstanceCache.Sort((a, b) =>
        {
            int byTime = a.Time.CompareTo(b.Time);
            return byTime != 0 ? byTime : a.Index.CompareTo(b.Index);
        });

        foreach (var slash in slashInstanceCache)
        {
            double age = time - slash.Time;
            if (age < 0 || age >= 1) continue;
            uint rgb = (uint)Math.Clamp(timeline.Get("set_slash_col", slash.Time), 0, 16777215);
            if (timeline.Get("col_convertion", slash.Time) == 0)
                rgb = ((rgb & 255) << 16) | (rgb & 0xFF00) | ((rgb >> 16) & 255);

            float width = (float)(12 * (1 - age) * (1 - age));
            if (rgb != 0)
                canvas.Blend(BlendFactor.InverseDestinationColor, BlendFactor.Zero);
            canvas.Line(-6, Timeline.Hash(slash.Seed) * 180, 326, Timeline.Hash(slash.Seed + 1) * 180, width, Color.Hex(rgb));
            // 反相目标色、目标系数取零；无论上面是否切换过，结束时都恢复常规混合。
            canvas.Blend(BlendFactor.SourceAlpha, BlendFactor.InverseSourceAlpha);
        }
    }
}
