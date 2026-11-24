// 按固定步长预计算自定义星体的出生、移动与可见状态。

namespace KuroakiGimmick.Core;

/// <summary>有界的 60 Hz 源模拟；禁用期间死亡状态与遮罩状态都保持冻结，不会被重新推进。</summary>
public sealed class CustomStarTimeline
{
    /// <summary>一颗星：Birth/Death 为秒，Speed 是每单位 travel 的位移；Top/Bottom 是 0xRRGGBB 颜色。</summary>
    /// <param name="Birth">生成秒数。</param>
    /// <param name="Death">消失秒数。</param>
    /// <param name="X">初始横坐标。</param>
    /// <param name="Y">初始纵坐标。</param>
    /// <param name="Speed">每单位累计移动量对应的位移。</param>
    /// <param name="Frame">精灵帧号。</param>
    /// <param name="Mask">是否属于遮罩层。</param>
    /// <param name="BirthTravel">出生时累计的移动量。</param>
    /// <param name="BirthFade">出生时累计的淡出量。</param>
    /// <param name="Top">顶部 RGB 颜色。</param>
    /// <param name="Bottom">底部 RGB 颜色。</param>
    public sealed record Star(double Birth, double Death, double X, double Y, double Speed, int Frame, bool Mask,
        double BirthTravel, double BirthFade, uint Top, uint Bottom);
    /// <summary>一次采样得到的星体绘制参数。</summary>
    /// <param name="X">逻辑横坐标。</param>
    /// <param name="Y">逻辑纵坐标。</param>
    /// <param name="Alpha">不透明度。</param>
    /// <param name="Frame">精灵帧号。</param>
    /// <param name="Top">顶部 RGB 颜色。</param>
    /// <param name="Bottom">底部 RGB 颜色。</param>
    /// <param name="Mask">是否绘制到遮罩层。</param>
    public sealed record Pose(double X, double Y, double Alpha, int Frame, uint Top, uint Bottom, bool Mask);
    /// <summary>已按固定步长生成的星体及其存活区间。</summary>
    public List<Star> Stars { get; } = [];
    readonly double[] maskTravel, maskFade, colorTravel;
    readonly Timeline timeline;
    readonly bool sourceStars;
    // Ticks 是原版的 60 Hz 逻辑 tick；模拟时长最多 3600 秒，星数上限 100000，超出改记诊断而不是继续分配。
    const int Ticks = 60, MaxStars = 100000;
    /// <summary>当前谱面是否启用星体模拟。</summary>
    public bool Enabled { get; }
    CustomStarTimeline(CustomStarTimeline source, Timeline owner)
    {
        timeline = owner;
        sourceStars = source.sourceStars;
        Enabled = source.Enabled;
        Stars = source.Stars;
        maskTravel = source.maskTravel;
        maskFade = source.maskFade;
        colorTravel = source.colorTravel;
    }
    internal CustomStarTimeline ForkForRender(Timeline owner) => new(this, owner);
    /// <summary>按有界的 60 Hz 时间网格预计算星体运动和淡出状态。</summary>
    /// <param name="timeline">用于采样轨道和回调的时间轴。</param>
    /// <param name="chart">提供音符、演出事件及诊断的谱面。</param>
    /// <param name="duration">要模拟的歌曲秒数。</param>
    /// <param name="enabled">是否启用预计算。</param>
    /// <param name="sourceStars">是否采用来源对象的星体规则。</param>
    /// <exception cref="InvalidDataException">预计算出的星体数量超过上限。</exception>
    public CustomStarTimeline(Timeline timeline, Chart chart, double duration, bool enabled, bool sourceStars = false)
    {
        this.timeline = timeline;
        Enabled = enabled;
        this.sourceStars = sourceStars;
        int count = enabled ? (int)Math.Ceiling(Math.Clamp(duration, 0, 3600) * Ticks) + 1 : 1;
        maskTravel = new double[count];
        maskFade = new double[count];
        colorTravel = new double[count];
        if (!enabled) return;
        var alive = new List<int>();
        double maskTimer = 0, colorTimer = 0;
        uint seed = 0;
        // 逐 tick 累加行程与淡出量：星的位置由“出生时的累计行程”与当前累计值之差决定，
        // 因此关闭遮罩期间行程停止累加，重新打开后星不会瞬移。
        for (int tick = 1; tick < count; tick++)
        {
            double t = tick / (double)Ticks, speed = timeline.Get("starspd_multiplier", t);
            bool mask = !sourceStars && timeline.Get("active_startrans", t) > 0,
                colored = sourceStars || timeline.Get("active_starchgcol", t) > 0;
            maskTravel[tick] = maskTravel[tick - 1] + (mask ? speed : 0);
            maskFade[tick] = maskFade[tick - 1] + (mask ? -.5 / Ticks * timeline.Get("startrans_alpha", t) : 0);
            colorTravel[tick] = colorTravel[tick - 1] + speed;
            for (int i = alive.Count - 1; i >= 0; i--)
            {
                int index = alive[i];
                var s = Stars[index];
                if (s.Mask && !mask) continue;
                double y = s.Y + s.Speed * ((s.Mask ? maskTravel[tick] : colorTravel[tick]) - s.BirthTravel);
                double alpha = s.Mask ? 2 + maskFade[tick] - s.BirthFade : Math.Min(1, 2 - (t - s.Birth) / 2)
                    * (sourceStars ? 1 : timeline.Get("starchgcol_alpha", t));
                if (alpha <= 0 || (s.Mask ? y < -10 || y > 190 : y >= 185))
                // 死亡时刻一旦写入就不再改变：之后把速度调反也不能让已销毁的星复活。
                {
                    Stars[index] = s with { Death = t };
                    alive.RemoveAt(i);
                }
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
                        sourceStars ? 0xffffff : Rgb("starchgcol_up_rgb"), sourceStars ? 0xffffff : Rgb("starchgcol_down_rgb")));
                    timer += interval;
                }
            }
            try
            {
                if (mask) Spawn(true, ref maskTimer);
                if (colored) Spawn(false, ref colorTimer);
            }
            catch (InvalidDataException ex)
            {
                chart.Diagnostics.Add(new("stars", 0, ex.Message, true));
                break;
            }
        }
    }
    /// <summary>按 60 Hz 表线性插值；表外的时间夹到两端，不外推。</summary>
    double Sample(double[] values, double time)
    {
        double frame = Math.Clamp(time * Ticks, 0, values.Length - 1);
        int i = (int)frame;
        return values[i] + (values[Math.Min(i + 1, values.Length - 1)] - values[i]) * (frame - i);
    }
    /// <summary>该时刻可见的星；Stars 按出生时间升序，因此 Birth 超过 time 即可提前结束遍历。</summary>
    /// <param name="time">采样时刻，单位为秒。</param>
    /// <param name="mask">是否查询遮罩层。</param>
    /// <returns>指定层内仍可见的星体姿态。</returns>
    public IEnumerable<Pose> At(double time, bool mask)
    {
        if (!Enabled || mask && timeline.Get("active_startrans", time) <= 0) yield break;
        double travel = Sample(mask ? maskTravel : colorTravel, time), fade = Sample(maskFade, time);
        foreach (var s in Stars)
        {
            if (s.Birth > time) break;
            if (s.Mask != mask || time >= s.Death) continue;
            double alpha = mask ? 2 + fade - s.BirthFade : Math.Min(1, 2 - (time - s.Birth) / 2)
                * (sourceStars ? 1 : timeline.Get("starchgcol_alpha", time));
            if (alpha > 0) yield return new(s.X, s.Y + s.Speed * (travel - s.BirthTravel), Math.Clamp(alpha, 0, 1), s.Frame, s.Top, s.Bottom, mask);
        }
    }
}
