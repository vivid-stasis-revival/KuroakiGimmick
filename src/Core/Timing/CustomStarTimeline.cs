namespace KuroakiGimmick.Core;

/// <summary>有界的 60 Hz 源模拟；禁用期间死亡状态与遮罩状态都保持冻结，不会被重新推进。</summary>
public sealed class CustomStarTimeline
{
    /// <summary>一颗星：Birth/Death 为秒，Speed 是每单位 travel 的位移；Top/Bottom 是 0xRRGGBB 颜色。</summary>
    public sealed record Star(double Birth, double Death, double X, double Y, double Speed, int Frame, bool Mask,
        double BirthTravel, double BirthFade, uint Top, uint Bottom);
    public sealed record Pose(double X, double Y, double Alpha, int Frame, uint Top, uint Bottom, bool Mask);
    public List<Star> Stars { get; } = [];
    readonly double[] maskTravel, maskFade, colorTravel;
    readonly Timeline timeline;
    // Ticks 是原版的 60 Hz 逻辑 tick；模拟时长最多 3600 秒，星数上限 100000，超出改记诊断而不是继续分配。
    const int Ticks = 60, MaxStars = 100000;
    public bool Enabled { get; }
    public CustomStarTimeline(Timeline timeline, Chart chart, double duration, bool enabled)
    {
        this.timeline = timeline; Enabled = enabled;
        int count = enabled ? (int)Math.Ceiling(Math.Clamp(duration, 0, 3600) * Ticks) + 1 : 1;
        maskTravel = new double[count]; maskFade = new double[count]; colorTravel = new double[count];
        if (!enabled) return;
        var alive = new List<int>(); double maskTimer = 0, colorTimer = 0; uint seed = 0;
        // 逐 tick 累加行程与淡出量：星的位置由“出生时的累计行程”与当前累计值之差决定，
        // 因此关闭遮罩期间行程停止累加，重新打开后星不会瞬移。
        for (int tick = 1; tick < count; tick++)
        {
            double t = tick / (double)Ticks, speed = timeline.Get("starspd_multiplier", t);
            bool mask = timeline.Get("active_startrans", t) > 0, colored = timeline.Get("active_starchgcol", t) > 0;
            maskTravel[tick] = maskTravel[tick - 1] + (mask ? speed : 0);
            maskFade[tick] = maskFade[tick - 1] + (mask ? -.5 / Ticks * timeline.Get("startrans_alpha", t) : 0);
            colorTravel[tick] = colorTravel[tick - 1] + speed;
            for (int i = alive.Count - 1; i >= 0; i--)
            {
                int index = alive[i]; var s = Stars[index]; if (s.Mask && !mask) continue;
                double y = s.Y + s.Speed * ((s.Mask ? maskTravel[tick] : colorTravel[tick]) - s.BirthTravel);
                double alpha = s.Mask ? 2 + maskFade[tick] - s.BirthFade : Math.Min(1, 2 - (t - s.Birth) / 2) * timeline.Get("starchgcol_alpha", t);
                if (alpha <= 0 || (s.Mask ? y < -10 || y > 190 : y >= 185))
                // 死亡时刻一旦写入就不再改变：之后把速度调反也不能让已销毁的星复活。
                { Stars[index] = s with { Death = t }; alive.RemoveAt(i); }
            }
            double interval = timeline.Get("starspawner_timer", t);
            if (interval <= 0 || !double.IsFinite(interval)) continue;
            void Spawn(bool isMask, ref double timer)
            {
                // 种子只由自增计数导出，保证每次烘焙出同一批星；生成点固定在左右两侧 110 像素宽的带内。
                timer--;
                while (timer <= 0)
                {
                    if (Stars.Count >= MaxStars) throw new InvalidDataException("Star simulation exceeds 100000 particles; increase starspawner_timer.");
                    uint n = unchecked(++seed * 31 + 1709);
                    double x = Math.Min(110, Math.Floor(Timeline.Hash(n) * 111)) + (Timeline.Hash(n + 1) < .5 ? 0 : 210);
                    double low = timeline.Get("starspd_low", t), high = timeline.Get("starspd_high", t);
                    double velocity = low + (high - low) * Timeline.Hash(n + 2);
                    uint Rgb(string name) => (uint)Math.Clamp(timeline.Get(name, t), 0, 16777215);
                    alive.Add(Stars.Count);
                    Stars.Add(new(t, double.PositiveInfinity, x, speed < 0 ? isMask ? 190 : 185 : isMask ? -10 : -5, velocity,
                        (int)Math.Min(3, Timeline.Hash(n + 3) * 4), isMask, isMask ? maskTravel[tick] : colorTravel[tick], maskFade[tick],
                        Rgb("starchgcol_up_rgb"), Rgb("starchgcol_down_rgb")));
                    timer += interval;
                }
            }
            try { if (mask) Spawn(true, ref maskTimer); if (colored) Spawn(false, ref colorTimer); }
            catch (InvalidDataException ex) { chart.Diagnostics.Add(new("stars", 0, ex.Message, true)); break; }
        }
    }
    /// <summary>按 60 Hz 表线性插值；表外的时间夹到两端，不外推。</summary>
    double Sample(double[] values, double time)
    {
        double frame = Math.Clamp(time * Ticks, 0, values.Length - 1); int i = (int)frame;
        return values[i] + (values[Math.Min(i + 1, values.Length - 1)] - values[i]) * (frame - i);
    }
    /// <summary>该时刻可见的星；Stars 按出生时间升序，因此 Birth 超过 time 即可提前结束遍历。</summary>
    public IEnumerable<Pose> At(double time, bool mask)
    {
        if (!Enabled || mask && timeline.Get("active_startrans", time) <= 0) yield break;
        double travel = Sample(mask ? maskTravel : colorTravel, time), fade = Sample(maskFade, time);
        foreach (var s in Stars)
        {
            if (s.Birth > time) break;
            if (s.Mask != mask || time >= s.Death) continue;
            double alpha = mask ? 2 + fade - s.BirthFade : Math.Min(1, 2 - (time - s.Birth) / 2) * timeline.Get("starchgcol_alpha", time);
            if (alpha > 0) yield return new(s.X, s.Y + s.Speed * (travel - s.BirthTravel), Math.Clamp(alpha, 0, 1), s.Frame, s.Top, s.Bottom, mask);
        }
    }
}
