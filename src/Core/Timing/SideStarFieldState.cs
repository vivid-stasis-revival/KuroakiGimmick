// 记录侧边星场实例的生成、重定位和随时间变化的绘制状态。

namespace KuroakiGimmick.Core;

/// <summary>以固定种子和 60 Hz 步长预计算 o_sg_star 状态，跳转不会改变随机序列。</summary>
public sealed class SideStarFieldState
{
    /// <summary>星体重新定位时的位移锚点。</summary>
    /// <param name="Time">定位时刻，单位为秒。</param>
    /// <param name="X">锚点横坐标。</param>
    /// <param name="Y">锚点纵坐标。</param>
    /// <param name="Travel">定位时累计的移动量。</param>
    public sealed record Position(double Time, double X, double Y, double Travel);
    /// <summary>侧边星体的出生状态、生命边界及重新定位记录。</summary>
    public sealed class Star
    {
        /// <summary>星体出生的歌曲秒数。</summary>
        public double Birth { get; init; }
        /// <summary>星体消失的歌曲秒数，未设边界时为正无穷。</summary>
        public double Death { get; set; } = double.PositiveInfinity;
        /// <summary>乘于累计运动量的星体速度。</summary>
        public double Speed { get; init; }
        /// <summary>选用的星体精灵帧号。</summary>
        public int Frame { get; init; }
        /// <summary>生成区域所在侧，0 为左侧。</summary>
        public int Side { get; init; }
        /// <summary>是否由自动生成器创建。</summary>
        public bool FromSpawner { get; init; }
        /// <summary>星体绘制使用的色相。</summary>
        public double Hue { get; init; }
        /// <summary>出生时的累计移动量。</summary>
        public double BirthTravel { get; init; }
        /// <summary>出生或重新定位后用于计算位移的锚点。</summary>
        public List<Position> Positions { get; } = [];
    }
    /// <summary>某时刻侧边星体的绘制参数。</summary>
    /// <param name="X">逻辑横坐标。</param>
    /// <param name="Y">逻辑纵坐标。</param>
    /// <param name="Angle">旋转角。</param>
    /// <param name="Alpha">不透明度。</param>
    /// <param name="Hue">色相。</param>
    /// <param name="Frame">精灵帧号。</param>
    /// <param name="FromSpawner">是否由生成器自动产生。</param>
    public sealed record Pose(double X, double Y, double Angle, double Alpha, double Hue, int Frame, bool FromSpawner);
    /// <summary>预计算的侧边星场实例。</summary>
    public List<Star> Stars { get; } = [];
    readonly Timeline timeline;
    readonly double[] travel;
    /// <summary>根据谱面回调与自动生成器预计算星体的创建和位置锚点。</summary>
    /// <param name="timeline">用于采样轨道和回调的时间轴。</param>
    /// <param name="chart">提供音符、演出事件及诊断的谱面。</param>
    /// <param name="duration">持续时间，单位为秒。</param>
    public SideStarFieldState(Timeline timeline, Chart chart, double duration)
    {
        this.timeline = timeline;
        int ticks = (int)Math.Ceiling(Math.Clamp(duration, 0, 3600) * 60);
        travel = new double[ticks + 1];
        var alive = new List<Star>();
        var events = timeline.Callbacks.Where(e => e.Name is "sides" or "starspawn").ToArray();
        int cursor = 0;
        uint seed = 27171;
        double timer = 0;
        double Random() => Timeline.Hash(unchecked(seed++));
        int Integer(int min, int max) => min + Math.Min(max - min, (int)(Random() * (max - min + 1)));
        void Spawn(double time, int tick, int side, bool automatic)
        {
            if (Stars.Count >= 100000) return;
            double x = Integer(0, 110) + 210 * side;
            double y = automatic ? Integer(-100, 80) : Integer(-10, 90);
            // 自动生成在 Create 之后才赋值 l，因此创建时计算色相使用其默认值 0。
            double hue = ((x * .6 + 180 - 62 * (automatic ? 0 : side)) * .7) / 255;
            double low = timeline.Get("starspd_low", time), high = timeline.Get("starspd_high", time);
            var star = new Star
            {
                Birth = time,
                Frame = Integer(0, 3),
                Side = side,
                FromSpawner = automatic,
                Hue = hue,
                Speed = low + (high - low) * Random(),
                BirthTravel = travel[tick]
            };
            star.Positions.Add(new(time, x, y, travel[tick]));
            Stars.Add(star);
            alive.Add(star);
        }
        for (int tick = 0; tick <= ticks; tick++)
        {
            double time = tick / 60.0;
            double pause = timeline.Get("star_pause", time);
            if (!double.IsFinite(pause)) pause = 0;
            if (tick > 0) travel[tick] = travel[tick - 1] + pause;
            while (cursor < events.Length && events[cursor].Time <= time)
            {
                var e = events[cursor++];
                if (e.Name == "sides")
                {
                    foreach (var star in alive)
                        star.Positions.Add(new(time, Integer(0, 110) + 210 * star.Side, Integer(0, 180), travel[tick]));
                }
                else
                {
                    double count = Math.Max(0, Math.Round(e.Value, MidpointRounding.ToEven));
                    if (!double.IsFinite(count) || count > 4096 || Stars.Count + count * 2 > 100000)
                    {
                        chart.Diagnostics.Add(new("native-stargazers", 0, "Star callback exceeds the source-preview particle budget.", true));
                        continue;
                    }
                    for (int pair = 0; pair < count; pair++)
                    {
                        Spawn(time, tick, 0, false);
                        Spawn(time, tick, 1, false);
                    }
                }
            }
            for (int i = alive.Count - 1; i >= 0; i--)
            {
                var star = alive[i];
                var at = star.Positions[^1];
                if (time - star.Birth >= 4 || at.Y + star.Speed * (travel[tick] - at.Travel) >= 185)
                {
                    star.Death = time;
                    alive.RemoveAt(i);
                }
            }
            double interval = timeline.Get("sg_starspawner", time);
            if (tick > 0 && RoomFxState.Enabled(pause) && double.IsFinite(interval) && interval > 0)
            {
                timer--;
                while (timer <= 0 && Stars.Count < 100000)
                {
                    Spawn(time, tick, Integer(0, 1), true);
                    timer += interval;
                }
            }
        }
        if (Stars.Count >= 100000) chart.Diagnostics.Add(new("native-stargazers", 0, "Star simulation exceeds 100000 particles.", true));
    }
    /// <summary>对指定时刻和生成来源枚举可见星体的绘制状态。</summary>
    /// <param name="time">采样时刻，单位为秒。</param>
    /// <param name="fromSpawner">是否选择由生成器自动创建的实例。</param>
    /// <returns>当前仍可见且来源匹配的星体姿态。</returns>
    public IEnumerable<Pose> At(double time, bool fromSpawner)
    {
        int tick = Math.Clamp((int)Math.Floor(Math.Max(0, time) * 60 + 1e-8), 0, travel.Length - 1);
        foreach (var star in Stars)
        {
            if (star.Birth > time) break;
            if (star.FromSpawner != fromSpawner || time >= star.Death) continue;
            var at = star.Positions.Last(p => p.Time <= time);
            double age = time - star.Birth;
            double alpha = Math.Clamp(Math.Min(2 * age, 2 - age / 2), 0, 1);
            if (alpha > 0) yield return new(at.X, at.Y + star.Speed * (travel[tick] - at.Travel),
                star.Speed * (travel[tick] - star.BirthTravel) * 4, alpha, star.Hue, star.Frame, star.FromSpawner);
        }
    }
    /// <summary>查询给定时刻之前的结束闪烁触发点。</summary>
    /// <param name="timeline">用于采样轨道和回调的时间轴。</param>
    /// <param name="time">采样时刻，单位为秒。</param>
    /// <returns>结束闪烁最近的起点，尚未触发时为 null。</returns>
    public static double? EndBlipBirth(Timeline timeline, double time) => timeline.Callbacks
        .LastOrDefault(e => e.Name == "sg_endblip" && e.Time <= time)?.Time;
}
