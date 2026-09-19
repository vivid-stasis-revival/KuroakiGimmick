namespace KuroakiGimmick.Core;

/// <summary>由对象定义驱动的时间轴行为；不依赖任何特定歌曲、精灵名或回调名。</summary>
public sealed partial class Timeline
{
    /// <summary>一条逐帧绑定：区间用拍，Value 是有界标量表达式，求值时才换算成秒。</summary>
    private sealed record FrameBinding(double StartBeat, double EndBeat, GimmickScalar Value);
    private readonly Dictionary<string, List<FrameBinding>> frameBindings = new(StringComparer.Ordinal);
    private readonly Dictionary<string, double> firstCallbacks = new(StringComparer.Ordinal);
    /// <summary>原版逻辑 tick 固定 60 Hz。粒子计时和速度都按 tick 定义，不随显示器帧率变化；改成按帧步进会改变轨迹。</summary>
    private const int LogicTicksPerSecond = 60;
    /// <summary>单次会话的粒子上限。超出后停止发射并记诊断，而不是继续分配直到内存耗尽。</summary>
    private const int MaximumObjectParticles = 100_000;

    /// <summary>一次建立输出名索引，避免每次读取 mod 时重新扫描全部逐帧函数。</summary>
    private void IndexObjectBehavior()
    {
        foreach (var callback in Callbacks)
        {
            // 只记第一次触发时刻：回调 fade 的起点由首次触发决定，后续同名回调不重启淡入。
            firstCallbacks.TryAdd(callback.Name, callback.Time);
        }
        if (Native?.Data is not { } definition)
        {
            return;
        }
        foreach (var function in chart.PerFrame)
        {
            if (!definition.PerFrameBindings.TryGetValue(function.Function, out var outputs))
            {
                continue;
            }
            foreach (var (name, scalar) in outputs)
            {
                if (!frameBindings.TryGetValue(name, out var bindings))
                {
                    frameBindings[name] = bindings = [];
                }
                bindings.Add(new(function.StartBeat, function.EndBeat, scalar));
            }
        }
    }

    /// <summary>
    /// 按固定逻辑 tick 烘焙粒子。回放/拖动/导出只查询同一结果，不重新取随机数。
    /// Death 记录第一次越界时刻，因此后续反向运动也不能让已销毁粒子复活。
    /// </summary>
    private void BuildObjectParticles(double duration, GimmickParticleEmitter emitter)
    {
        double timer = 0;
        int emissionIndex = 0;
        bool invalidIntervalReported = false;
        int frameCount = Native!.Sprites[emitter.Sprite].Frames.Count;
        for (int tick = 1; tick <= Math.Ceiling(duration * LogicTicksPerSecond); tick++)
        {
            // 时间由 tick 序号导出，而不是累加浮点步长：累加会让长曲子末尾产生漂移，破坏可复现性。
            double time = tick / (double) LogicTicksPerSecond;
            double interval = Get(emitter.IntervalMod, time);
            if (!double.IsFinite(interval) || interval <= 0)
            {
                if (!invalidIntervalReported)
                {
                    chart.Diagnostics.Add(new("native-gimmick/particles", 0,
                        "Emission interval must be positive; emission pauses while the value is invalid.", true));
                }
                invalidIntervalReported = true;
                timer = 0;
                continue;
            }
            timer++;
            while (timer >= interval)
            {
                if (ObjectParticles.Count + emitter.ParticlesPerEmission > MaximumObjectParticles)
                {
                    chart.Diagnostics.Add(new("native-gimmick/particles", 0,
                        "Particle emission exceeds 100000 particles/session; later particles are omitted.", true));
                    return;
                }
                timer -= interval;
                // 种子只由发射序号导出，因此同一份谱面每次烘焙出完全相同的粒子；每个粒子再按固定步长取子种子。
                uint seed = unchecked((uint) emissionIndex++ * 31 + emitter.Seed);
                float x = MathF.Floor(Hash(seed) * (float)(emitter.Width + 1));
                float y = MathF.Floor(Hash(seed + 1) * (float)(emitter.Height + 1));
                for (uint index = 0; index < emitter.ParticlesPerEmission; index++)
                {
                    uint particle = seed + 2 + index * 5;
                    float vx = (Hash(particle) * 2 - 1) * (float) emitter.Speed;
                    float vy = (Hash(particle + 1) * 2 - 1) * (float) emitter.Speed;
                    double death = time + emitter.Lifetime;
                    // 速度是“每 tick 位移”，所以位置按 step 个 tick 线性外推，而不是乘以秒数。
                    for (int step = 1; step <= Math.Ceiling(emitter.Lifetime * LogicTicksPerSecond); step++)
                    {
                        float px = x + vx * step, py = y + vy * step;
                        if (px < -emitter.Margin || px > emitter.Width + emitter.Margin || py < -emitter.Margin
                            || py > emitter.Height + emitter.Margin)
                        {
                            death = Math.Min(death, time + step / (double) LogicTicksPerSecond);
                            break;
                        }
                    }
                    float saturation = emitter.SaturationMod == null ? 0 : (float) Math.Clamp(Get(emitter.SaturationMod, time), 0, 1);
                    ObjectParticles.Add(new(time, x, y, vx, vy, Math.Min(frameCount - 1, (int)(Hash(particle + 2) * frameCount)),
                        MathF.Floor(Hash(particle + 3) * 256) / 255, saturation, death, Life: emitter.Lifetime));
                }
            }
        }
    }
}

