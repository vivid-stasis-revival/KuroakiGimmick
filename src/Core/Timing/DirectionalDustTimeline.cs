// 依据后段回调建立可随机访问的定向尘埃时间轴。

using System;
using System.Collections.Generic;
using System.Linq;

namespace KuroakiGimmick.Core;

/// <summary>根据 obj_starcrashers_gimmick.Create、Particle_Spawners 与 o_pt_diamonddust_invmask.Step 重建后段钻尘。来源 GML 未提供原始随机状态及 global.op_bgparticles 设置，预览使用固定种子与默认粒子密度以支持重复跳转。</summary>
public sealed class DirectionalDustTimeline
{
    /// <summary>定向尘埃粒子的生成状态。</summary>
    /// <param name="Birth">生成 tick。</param>
    /// <param name="Death">消失 tick。</param>
    /// <param name="X">起始横坐标。</param>
    /// <param name="Y">起始纵坐标。</param>
    /// <param name="Vx">水平速度。</param>
    /// <param name="Frame">粒子精灵帧号。</param>
    public readonly record struct Dust(int Birth, int Death, float X, float Y, float Vx, int Frame);
    /// <summary>按出生顺序存放的定向尘埃实例。</summary>
    public List<Dust> Diamonds { get; } = [];
    const int MaxDust = 25000;
    private uint seed = 0xC76E9A21;
    private readonly Timeline timeline;

    // 运动使用 60 Hz 逻辑步长，独立于渲染帧间隔。
    /// <summary>根据时间轴事件预计算固定步长的尘埃生成状态。</summary>
    /// <param name="session">提供谱面、时间轴及已加载资源的会话。</param>
    public DirectionalDustTimeline(Session session) : this(session.Timeline, session.Chart, session.Duration)
    {
    }

    /// <summary>根据时间轴事件预计算固定步长的尘埃生成状态。</summary>
    /// <param name="timeline">用于采样轨道和回调的时间轴。</param>
    /// <param name="chart">提供音符、演出事件及诊断的谱面。</param>
    /// <param name="duration">持续时间，单位为秒。</param>
    public DirectionalDustTimeline(Timeline timeline, Chart chart, double duration)
    {
        this.timeline = timeline;
        var events = chart.Mods
            .Where(e => e.Name == "chorus_satalli_alli_particles" || e.Name == "chorus_satalli_sat_particles")
            .OrderBy(e => e.Beat).ThenBy(e => e.Order);
        int finalTick = Math.Clamp((int)Math.Ceiling(duration * 60), 0, 60 * 3600);
        foreach (var evt in events)
        {
            int birth = Math.Max(0, (int)Math.Ceiling(timeline.Bpm.Time(evt.Beat) * 60 - 1e-8));
            bool fromLeft = evt.Name == "chorus_satalli_alli_particles";
            // addExtraMod 的回调参数顺序为起点、时长、v1、v2、ease，因此 repeat(arg3) 读取 V2/To 终值。
            int repeats = double.IsFinite(evt.To) ? (int)Math.Clamp(Math.Round(evt.To, MidpointRounding.ToEven), 0, 2500) : 0;
            for (int n = 0; n < repeats && Diamonds.Count < MaxDust; n++)
            {
                float y = KenomaGMK.Runtime.Particles.ParticleMotion.DirectionalY(ref seed); // 每次调用 spawn_particles_directional 只取一次 irandom_range()。
                // 原版从 0 迭代到 1/op_bgparticles 并包含终点，默认值为 1 时每次发射两个实例。
                for (int duplicate = 0; duplicate < KenomaGMK.Runtime.Particles.ParticleMotion.Count(1, 1) && Diamonds.Count < MaxDust; duplicate++)
                {
                    var (x, speed, frame) = KenomaGMK.Runtime.Particles.ParticleMotion.DirectionalChild(fromLeft, ref seed);
                    var dust = new Dust(birth, finalTick + 1, (float)x, y, (float)speed, frame);
                    for (int tick = birth; tick <= finalTick; tick++)
                        if (KenomaGMK.Runtime.Particles.ParticleMotion.InInverseMask(PositionAt(dust, tick).X))
                        {
                            dust = dust with { Death = tick };
                            break;
                        }
                    Diamonds.Add(dust);
                }
            }
        }
        Diamonds.Sort(static (a, b) => a.Birth.CompareTo(b.Birth));
    }

    /// <summary>结合星尘初始位置与粒子偏移计算指定 tick 的位置。</summary>
    /// <param name="dust">待采样的尘埃实例。</param>
    /// <param name="tick">目标逻辑 tick。</param>
    /// <returns>该 tick 的逻辑坐标。</returns>
    public (float X, float Y) PositionAt(Dust dust, int tick)
    {
        var offset = timeline.ParticleOffset(tick / 60.0) - timeline.ParticleOffset(Math.Max(0, dust.Birth - 1) / 60.0);
        return (dust.X + dust.Vx * (tick - dust.Birth + 1) + offset.X, dust.Y + offset.Y);
    }

    /// <summary>通过二分查询定位出生 tick 不早于指定值的首项。</summary>
    /// <param name="dust">按出生 tick 排序的粒子。</param>
    /// <param name="tick">包含在查询中的最早 tick。</param>
    /// <returns>首个不早于该 tick 的索引。</returns>
    public static int FirstSince(List<Dust> dust, int tick)
    {
        int lo = 0, hi = dust.Count;
        while (lo < hi)
        {
            int mid = (lo + hi) / 2;
            if (dust[mid].Birth < tick) lo = mid + 1;
            else hi = mid;
        }
        return lo;
    }
}
