namespace KuroakiGimmick.Core;

/// <summary>重建 Plaudite 的曲绘锁存、红色粒子的持续状态及定向爆发。</summary>
public sealed class LayeredParticlesState
{
    /// <summary>粒子前后层切换记录。</summary>
    /// <param name="Time">变更生效时刻。</param>
    /// <param name="Front">是否绘制在前层。</param>
    public readonly record struct RedWrite(double Time, bool Front);
    /// <summary>红色粒子前后层状态的变更记录。</summary>
    public List<RedWrite> RedWrites { get; } = [];
    /// <summary>由时间轴触发的粒子爆发事件。</summary>
    public List<Timeline.Dust> Bursts { get; } = [];
    /// <summary>结尾演出开始的时刻。</summary>
    public double EndingTime { get; }

    /// <summary>从谱面与时间轴预计算粒子分层及结尾状态。</summary>
    /// <param name="timeline">用于采样轨道和回调的时间轴。</param>
    public LayeredParticlesState(Timeline timeline)
    {
        EndingTime = timeline.Callbacks.FirstOrDefault(e => e.Name == "plaudite_ending_event")?.Time ?? double.PositiveInfinity;
        for (int tick = 0; tick <= Math.Ceiling(Math.Min(3600, timeline.End) * 60); tick++)
        {
            double time = tick / 60.0;
            if (RoomFxState.Enabled(timeline.Get("plaudite_red_particle", time)))
                RedWrites.Add(new(time, timeline.Get("plaudite_sect7bg", time) > .5));
        }
        foreach (var callback in timeline.Callbacks.Where(e => e.Name == "plaudite_pburst"))
        {
            double requested = Math.Ceiling(Math.Max(0, callback.Value));
            if (requested > 50_000 || Bursts.Count + requested * 2 > 100_000)
            {
                timeline.Native?.Fail("plaudite-pburst", "Directional particle count exceeds the 100000-instance preview budget.");
                continue;
            }
            // 原版 spawn_particles_directional 的循环包含 amount/bgparticles 端点，因此默认高画质下每次外层循环生成两个粒子。
            for (int group = 0; group < requested; group++)
            {
                uint seed = unchecked((uint)callback.Index * 65537 + (uint)group * 83 + 1741);
                float x = MathF.Floor(Timeline.Hash(seed) * 321);
                for (uint child = 0; child < 2; child++)
                {
                    uint random = seed + child * 13 + 1;
                    float velocity = (float)(Timeline.Hash(random) * .75 * timeline.Get("pburstspeed", callback.Time));
                    var particle = new Timeline.Dust(callback.Time, x, -10, 0, velocity,
                        Timeline.DiamondFrame(Timeline.Hash(random + 1)), MathF.Floor(Timeline.Hash(random + 2) * 256) / 255,
                        (float)Math.Clamp(timeline.Get("rainbow", callback.Time), 0, 1), callback.Time + 4, true, 4, true);
                    double death = particle.Death;
                    var startOffset = timeline.ParticleOffset(particle.Time);
                    for (int tick = 1; tick <= 240; tick++)
                    {
                        double time = particle.Time + tick / 60.0;
                        var offset = timeline.ParticleOffset(time) - startOffset;
                        double y = particle.Y + particle.Vy * tick + offset.Y;
                        if (particle.X + offset.X is < -50 or > 370 || y is < -50 or > 230)
                        {
                            death = time;
                            break;
                        }
                    }
                    Bursts.Add(particle with { Death = death });
                }
            }
        }
        Bursts.Sort((a, b) => a.Time.CompareTo(b.Time));
    }

    /// <summary>根据出生时刻和采样时刻确定粒子的颜色与前后层。</summary>
    /// <param name="birth">粒子出生秒数。</param>
    /// <param name="time">采样时刻，单位为秒。</param>
    /// <returns>当前粒子是否为红色以及是否属于前层。</returns>
    public (bool Red, bool Front) ParticleState(double birth, double time)
    {
        int index = SortedSearch.UpperBound(RedWrites, time, x => x.Time) - 1;
        return index >= 0 && RedWrites[index].Time >= birth ? (true, RedWrites[index].Front) : (false, false);
    }
}
