namespace KuroakiGimmick.Core;

/// <summary>
/// 无显示状态的时间轴。构建时按拍数和声明顺序整理轨道；查询不依赖上一帧，因此预览、倒拖和导出可以共享结果。
/// </summary>
public sealed partial class Timeline
{
    /// <summary>侧边爆发粒子：跟随 particlexpower/particleypower 的整体位移，寿命固定 4 秒（240 个 60 Hz tick）。</summary>
    void BuildSideBursts(double duration)
    {
        int added = 0;
        foreach (var e in chart.Mods.Where(e => e.Name is "pburstleft" or "pburstright").OrderBy(e => e.Beat).ThenBy(e => e.Order))
        {
            double time = Bpm.Time(e.Beat);
            if (time > duration || e.To <= 0)
            {
                continue;
            }
            // GML 的 repeat 用的是四舍五入后的次数（与 InitMisc 的 for 循环不同）。
            double groups = Math.Round(e.To, MidpointRounding.ToEven);
            if (groups > 4096 || added + groups * 2 > 100000)
            {
                chart.Diagnostics.Add(new("particles", e.Order + 1,
                    "Side burst exceeds 4096 groups/event or 100000 particles/session; callback skipped.", true));
                continue;
            }
            // 左右爆发的原版发射点 x=130/190，方向相反；这些都在 320×180 逻辑空间里。
            float x = e.Name == "pburstleft" ? 130 : 190, dir = e.Name == "pburstleft" ? -1 : 1, speed = (float) Get("pburstspeed", time);
            for (int group = 0; group < groups; group++)
            {
                uint seed = unchecked((uint) e.Order * 3011 + (uint) group * 23 + 1717);
                float y = MathF.Floor(Hash(seed) * 281) - 50;
                float power = dir * 1.5f * (3 + Hash(seed + 1) * 2) * speed;
                for (uint i = 0; i < 2; i++)
                {
                    float vx = Hash(seed + 2 + i * 2) * .5f * power;
                    double death = time + 4;
                    var born = ParticleOffset(time);
                    // 4 秒寿命 = 240 个 tick；出界判定留 50 像素余量，记录第一次越界的时刻作为死亡时间。
                    for (int frame = 1; frame <= 240; frame++)
                    {
                        double at = time + frame / 60.0;
                        var offset = ParticleOffset(at) - born;
                        float px = x + vx * frame + offset.X, py = y + offset.Y;
                        if (px < -50 || px > 370 || py < -50 || py > 230)
                        {
                            death = at;
                            break;
                        }
                    }
                    Particles.Add(new(time, x, y, vx, 0, (int) Math.Min(3, Hash(seed + 3 + i * 2) * 4), 0, 0, death, true, 4, true));
                    added++;
                }
            }
        }
    }

    /// <summary>plaudite 爆发粒子：一次性触发与倒计时重复触发两条路径，都按 60 Hz tick 推进。</summary>
    void BuildBursts(double duration)
    {
        int added = 0; bool budgetReported = false;
        var events = chart.Mods.Where(e => e.Name == "plaudite_pburst" && e.Proxy == -1).OrderBy(e => e.Beat).ThenBy(e => e.Order).ToArray();
        void Emit(double time, double groups, uint seedBase)
        {
            if (groups > 4096 || added + Math.Ceiling(groups) * 2 > 100000)
            {
                if (!budgetReported) chart.Diagnostics.Add(new("particles", 0, "Particle burst budget exceeded (4096 groups/event or 100000 particles/session); excess emissions omitted.", true));
                budgetReported = true; return;
            }
            float speed = (float)Get("pburstspeed", time);
            for (int group = 0; group < Math.Ceiling(groups); group++)
            {
                uint seed = unchecked(seedBase + (uint)group * 19 + 8191);
                float x = Math.Min(320, MathF.Floor(Hash(seed) * 321));
                for (uint i = 0; i < 2; i++)
                {
                    float vy = Hash(seed + 1 + i * 3) * .75f * speed;
                    double death = time + 2;
                    // 出界时刻按整 tick 解算：向下移动看下边界 240，向上看上边界 40，都换算成 60 Hz 的 tick 数。
                    if (vy > 0) death = Math.Min(death, time + (Math.Floor(240 / vy) + 1) / 60);
                    else if (vy < 0) death = Math.Min(death, time + (Math.Floor(40 / -vy) + 1) / 60);
                    Particles.Add(new(time, x, -10, 0, vy, (int)Math.Min(3, Hash(seed + 2 + i * 3) * 4), 0, 0, death, true));
                    added++;
                }
            }
        }
        foreach (var e in events.Where(e => e.From <= 0 && e.To > 0))
        {
            double time = Bpm.Time(e.Beat);
            if (time <= duration) Emit(time, e.To, unchecked((uint)e.Order * 3079));
        }
        var repeated = events.Where(e => e.From > 0).Select(e => (Start: Bpm.Time(e.Beat), Milliseconds: e.To, e.Order)).ToArray();
        if (repeated.Length == 0) return;
        var active = new List<(double Start, double Milliseconds, int Order)>();
        int cursor = 0; double countdown = -.01;
        // cc 先更新已有的倒计时 tween，再派发新回调。
        // 并存的 tween 保持源顺序：后声明的短 tween 可能先结束，而更早的长 tween 仍然存活。
        for (int tick = 0; tick <= Math.Ceiling(duration * 60); tick++)
        {
            double time = tick / 60.0;
            foreach (var e in active)
            {
                double progress = e.Milliseconds <= 0 ? 1 : Math.Clamp((time - e.Start) * 1000 / e.Milliseconds, 0, 1);
                countdown = e.Milliseconds + (-.01 - e.Milliseconds) * progress;
            }
            active.RemoveAll(e => time >= e.Start + Math.Max(0, e.Milliseconds) / 1000);
            while (cursor < repeated.Length && repeated[cursor].Start <= time) active.Add(repeated[cursor++]);
            // 倒计时高于 0.5 才继续发射：这是原版的判定阈值，不是随手选的容差。
            if (countdown >= .5) Emit(time, 1, unchecked((uint)tick * 1031 + 93017));
        }
    }

    /// <summary>
    /// 常驻背景粒子，同时预累加 particlexpower/particleypower 的逐帧位移表。
    /// 位移表按 60 Hz tick 累加，粒子的出界判定要用它，所以必须先于发射建立。
    /// </summary>
    void BuildCustomParticles(double duration, bool emit = true)
    {
        particleOffsets.Add(System.Numerics.Vector2.Zero);
        int frames = (int) Math.Ceiling(duration * 60);
        for (int frame = 1; frame <= frames; frame++)
        {
            double t = frame / 60.0;
            particleOffsets.Add(particleOffsets[^1] + new System.Numerics.Vector2((float) Get("particlexpower", t), (float) Get("particleypower",
                t)));
        }
        if (!emit)
        {
            return;
        }
        // o_csm_particle_system：每两个 60 Hz tick 调用一次生成。
        // spawn_particles_area(...,1,1.5,1)，High/默认 bgparticles=1：
        // 同一个随机位置生成两颗粒子，速度和帧号各自独立。
        for (int frame = 2; frame <= frames; frame += 2)
        {
            double t = frame / 60.0;
            uint n = (uint)(frame * 19 + 271);
            float x = MathF.Floor(Hash(n) * 321), y = MathF.Floor(Hash(n + 1) * 181);
            for (uint i = 0; i < 2; i++)
            {
                uint seed = n + 2 + i * 5;
                float vx = (Hash(seed) * 2 - 1) * 1.5f, vy = (Hash(seed + 1) * 2 - 1) * 1.5f;
                double death = t + 2;
                // 记住第一次越界的 tick。之后反向的位移 mod 不能让已经被游戏销毁的粒子复活。
                for (int at = frame + 1; at <= Math.Min(frame + 120, frames); at++)
                {
                    var offset = particleOffsets[at] - particleOffsets[frame];
                    float px = x + vx * (at - frame) + offset.X, py = y + vy * (at - frame) + offset.Y;
                    if (px < -50 || px > 370 || py < -50 || py > 230)
                    {
                        death = at / 60.0;
                        break;
                    }
                }
                Particles.Add(new(t, x, y, vx, vy, (int) Math.Min(3, Hash(seed + 2) * 4), MathF.Floor(Hash(seed + 3) * 256) / 255f,
                    (float) Math.Clamp(Get("rainbow", t), 0, 1), death));
            }
        }
    }

    /// <summary>按 60 Hz 表插值出该时刻的整体粒子位移；表外的时间夹到两端，不外推。</summary>
    public System.Numerics.Vector2 ParticleOffset(double time)
    {
        if (particleOffsets.Count == 0)
        {
            return default;
        }
        double frame = Math.Clamp(time * 60, 0, particleOffsets.Count - 1);
        int i = (int) frame;
        return System.Numerics.Vector2.Lerp(particleOffsets[i], particleOffsets[Math.Min(i + 1, particleOffsets.Count - 1)], (float)(frame - i));
    }
}
