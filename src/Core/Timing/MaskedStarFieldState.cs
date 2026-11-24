// 在 CPU 上以 60 Hz 重建遮罩星场、棋盘变化和烟花实例。

using System;
using System.Collections.Generic;
using System.Linq;

namespace KuroakiGimmick.Core;

/// <summary>
/// 依据 v6.2.0.1 GML，以固定 60 Hz 预计算 Starcrashers 的 o_sc_* 对象。
/// 预览与跳转复用同一批实例。随机分布使用可重复的替代种子，原 GameMaker 会话种子未知。
/// </summary>
public sealed class MaskedStarFieldState
{
    /// <summary>星场模拟每秒的固定逻辑 tick 数。</summary>
    public const int TicksPerSecond = 60;
    const int MaxTicks = 60 * 3600;
    const int MaxStars = 40000;
    const int MaxEffects = 30000;
    /// <summary>遮罩星体的初始状态与存活区间。</summary>
    /// <param name="Birth">生成 tick。</param>
    /// <param name="Death">销毁边界 tick。</param>
    /// <param name="X">起始横坐标。</param>
    /// <param name="Y">起始纵坐标。</param>
    /// <param name="Speed">乘于累计移动量的速度。</param>
    /// <param name="Intro">是否使用开场星体的运动规则。</param>
    /// <param name="InitialAngle">初始旋转角。</param>
    public sealed record Star(int Birth, int Death, double X, double Y, double Speed, bool Intro, double InitialAngle);
    /// <summary>由回调触发的中央矩形。</summary>
    /// <param name="Birth">触发 tick。</param>
    /// <param name="Red">为 true 时使用红色，否则使用黑色。</param>
    public sealed record CenterRect(int Birth, bool Red);
    /// <summary>横跨画面的斩线端点。</summary>
    /// <param name="Birth">生成 tick。</param>
    /// <param name="TopX">顶端横坐标。</param>
    /// <param name="BottomX">底端横坐标。</param>
    public sealed record Slash(int Birth, double TopX, double BottomX);
    /// <summary>从发射到爆裂的烟花轨迹。</summary>
    /// <param name="Birth">发射 tick。</param>
    /// <param name="Burst">爆裂 tick。</param>
    /// <param name="X">发射所在横坐标。</param>
    /// <param name="EndpointY">爆裂点纵坐标。</param>
    public sealed record Firework(int Birth, int Burst, double X, double EndpointY);
    /// <summary>烟花爆裂后单个碎片的初始状态。</summary>
    /// <param name="Birth">生成 tick。</param>
    /// <param name="Death">销毁边界 tick。</param>
    /// <param name="X">起始横坐标。</param>
    /// <param name="Y">起始纵坐标。</param>
    /// <param name="Frame">精灵帧号。</param>
    /// <param name="Direction">初始运动方向，单位为度。</param>
    /// <param name="Speed">初始速度。</param>
    public sealed record Fragment(int Birth, int Death, double X, double Y, int Frame, double Direction, double Speed);
    /// <summary>棋盘遮罩的一次刷新快照。</summary>
    /// <param name="Tick">刷新 tick。</param>
    /// <param name="Pixels">按格记录的开关状态。</param>
    /// <param name="Revision">区分不同刷新结果的版本号。</param>
    public sealed record Checker(int Tick, bool[] Pixels, int Revision);
    /// <summary>按生成时间排列的星体，存活区间以 tick 表示。</summary>
    public List<Star> Stars { get; } = [];
    /// <summary>由红色或黑色中央矩形回调生成的实例。</summary>
    public List<CenterRect> CenterRects { get; } = [];
    /// <summary>激光斩线的生成时刻和两端坐标。</summary>
    public List<Slash> Slashes { get; } = [];
    /// <summary>烟花发射与爆裂的事件表。</summary>
    public List<Firework> Fireworks { get; } = [];
    /// <summary>爆裂时生成的碎片初始状态。</summary>
    public List<Fragment> Fragments { get; } = [];
    /// <summary>每次更新后的棋盘格遮罩快照。</summary>
    public List<Checker> Checkers { get; } = [];
    /// <summary>在按出生 tick 升序排列的实例中二分定位可见候选的起点。</summary>
    /// <typeparam name="T">被索引的模拟实例类型。</typeparam>
    /// <param name="items">按出生 tick 排序的实例列表。</param>
    /// <param name="minimumTick">包含在候选范围内的最早 tick。</param>
    /// <param name="born">从实例取得出生 tick 的函数。</param>
    /// <returns>首个不早于下界的索引，全部较早时为列表长度。</returns>
    public static int FirstBornAtOrAfter<T>(List<T> items, int minimumTick, Func<T, int> born)
    {
        int lo = 0, hi = items.Count;
        while (lo < hi)
        {
            int mid = (lo + hi) / 2;
            if (born(items[mid]) < minimumTick) lo = mid + 1;
            else hi = mid;
        }
        return lo;
    }
    private readonly double[] movementPrefix;
    private readonly Timeline timeline;
    private uint random;
    private int nextCheckerRevision;
    private bool nextFireworkRight;

    /// <summary>对当前会话的谱面和时间轴预计算星场。</summary>
    /// <param name="session">提供谱面、时间轴与时长的会话。</param>
    public MaskedStarFieldState(Session session) : this(session.Timeline, session.Chart, session.Duration)
    {
    }

    /// <summary>按固定步长模拟回调、星体及爆裂事件，最多推进一小时。</summary>
    /// <param name="sourceTimeline">采样速度和生成间隔的时间轴。</param>
    /// <param name="chart">包含生成回调的谱面。</param>
    /// <param name="duration">要预计算的秒数。</param>
    public MaskedStarFieldState(Timeline sourceTimeline, Chart chart, double duration)
    {
        timeline = sourceTimeline;
        int ticks = Math.Clamp((int)Math.Ceiling(Math.Max(0, duration) * TicksPerSecond), 0, MaxTicks);
        movementPrefix = new double[ticks + 1];
        // 1. 预计算完整的位移前缀，才能判断星体是否已在后续反向运动前越过销毁边界。
        for (int tick = 1; tick <= ticks; tick++)
            movementPrefix[tick] = movementPrefix[tick - 1] + Finite(timeline.Get("starspd_multiplier", tick / 60.0), 1);
        // 2. 固定替代种子，并将源事件的拍数转换为有序的模拟 tick。
        random = 0x5A7C8E13u;
        var events = chart.Mods
            .Where(e => IsInstanceCallback(e.Name))
            .OrderBy(e => e.Beat).ThenBy(e => e.Order)
            .Select(e => (Tick: Math.Max(1, (int)Math.Ceiling(timeline.Bpm.Time(e.Beat) * 60 - 1e-8)), e.Name, e.To))
            .ToArray();
        int eventIndex = 0;
        var checkerWrites = SpriteDialogueState.Writes(timeline, "lasthours_checker_set").Where(x => x.Value > 0 && x.Time > 0)
            .Select(x => Math.Max(1, (int)Math.Ceiling(x.Time * 60 - 1e-8))).Distinct().ToArray();
        int checkerIndex = 0;
        double spawnTimer = 0;
        // 3. 首次 Draw 无条件建立棋盘，随后逐 tick 消费回调并推进自动生成器。
        RefreshChecker(0);
        for (int tick = 1; tick <= ticks; tick++)
        {
            double time = tick / 60.0;
            while (eventIndex < events.Length && events[eventIndex].Tick <= tick)
            {
                var e = events[eventIndex++];
                switch (e.Name)
                {
                    case "spawn_star_mask":
                        IntroStar(tick, Int(5, 105), Int(5, 160));
                        IntroStar(tick, Int(215, 315), Int(5, 160));
                        break;
                    case "spawn_centerrect_red":
                        if (CenterRects.Count < MaxEffects) CenterRects.Add(new(tick, true));
                        break;
                    case "spawn_centerrect_black":
                        if (CenterRects.Count < MaxEffects) CenterRects.Add(new(tick, false));
                        break;
                    case "laser_spawn":
                        if (Slashes.Count < MaxEffects)
                        {
                            int side = Int(0, 1) * 207;
                            Slashes.Add(new(tick, Real(0, 110) + side, Real(0, 110) + side));
                        }
                        break;
                    case "chorus_firework":
                        if (Fireworks.Count < MaxEffects)
                        {
                            FireworkSpawn(tick, Int(10, 104) + (nextFireworkRight ? 206 : 0), Int(20, 90));
                            nextFireworkRight = !nextFireworkRight;
                        }
                        break;
                }
            }
            while (checkerIndex < checkerWrites.Length && checkerWrites[checkerIndex] <= tick)
            {
                RefreshChecker(tick);
                checkerIndex++;
            }
            // o_sc_starspawner.Step_0: timer -= cc.timediff*60, while(timer<=0)
            double interval = Finite(timeline.Get("starspawner_timer", time), 0);
            if (interval > 0)
            {
                spawnTimer -= 1;
                int guard = 0;
                while (spawnTimer <= 0 && guard++ < 100 && Stars.Count < MaxStars)
                {
                    int side = Int(0, 1);
                    SpawnStar(tick, Int(0, 110) + 210 * side, -5);
                    spawnTimer += Math.Max(0.4, interval);
                }
            }
        }
    }

    /// <summary>判断回调是否生成该星场模拟处理的实例。</summary>
    /// <param name="name">待识别的回调名。</param>
    /// <returns>该回调由模拟器生成实例时为 true。</returns>
    public static bool IsInstanceCallback(string name) => name is "spawn_star_mask" or "spawn_centerrect_red" or
        "spawn_centerrect_black" or "laser_spawn" or "chorus_firework";
    static double Finite(double v, double fallback) => double.IsFinite(v) ? v : fallback;
    // 使用显式的 xorshift32 作为可重复的替代随机源，无法重现 GameMaker 会话的随机状态。
    double Unit()
    {
        random ^= random << 13;
        random ^= random >> 17;
        random ^= random << 5;
        return random / 4294967296.0;
    }
    int Int(int low, int high) => low + (int)(Unit() * (high - low + 1));
    double Real(double low, double high) => low + Unit() * (high - low);
    void SpawnStar(int tick, double x, double y, bool intro = false)
    {
        if (Stars.Count >= MaxStars) return;
        double time = tick / 60.0;
        double speed = Real(Finite(timeline.Get("starspd_low", time), 3), Finite(timeline.Get("starspd_high", time), 5));
        int death = intro ? Math.Min(movementPrefix.Length, tick + 4 * 60) : movementPrefix.Length;
        if (!intro)
        {
            // 在 Step_0 首次不再满足存活条件的 tick 销毁实例，保留离散步进语义。
            for (int t = tick; t < movementPrefix.Length; t++)
            {
                double age = (t - tick + 1) / 60.0;
                if (Math.Min(1, 2 - age / 2) <= 0 ||
                    y + speed * (movementPrefix[t] - movementPrefix[tick - 1]) >= 185)
                {
                    death = t;
                    break;
                }
            }
        }
        Stars.Add(new(tick, death, x, y, speed, intro, 0));
    }
    void IntroStar(int tick, double x, double y) => SpawnStar(tick, x, y, true);
    void FireworkSpawn(int birth, double x, double endpointY)
    {
        int burst = birth + (int)Math.Ceiling(.34 * 60);
        Fireworks.Add(new(birth, burst, x, endpointY));
        // o_sc_firework.Step_0 在最后一个 tick 生成 30 个粒子。
        for (int i = 0; i < 30 && Fragments.Count < MaxEffects * 30; i++)
        {
            int frame = Int(0, 3);
            double angle = Real(0, 360);
            double speed = Real(2.8, 4.8);
            double y = endpointY; // 出生 tick 的绘制位置；计时器达到 1 时取插值终点。
            double direction = angle * Math.PI / 180;
            int death = movementPrefix.Length;
            // 按 GML Step_0 的纵向约定，以负正弦乘以距离。
            for (int t = burst; t < movementPrefix.Length; t++)
            {
                double age = (t - burst) / 60.0;
                double py = y - Math.Sin(direction) * FragmentDistance(age, speed) + FragmentGravity(age);
                if (py >= 180)
                {
                    death = t;
                    break;
                }
            }
            Fragments.Add(new(burst, death, x, y, frame, direction, speed));
        }
    }
    /// <summary>由经过秒数和初速度计算碎片沿初始方向的位移。</summary>
    /// <param name="time">碎片出生后经过的秒数。</param>
    /// <param name="speed">初始速度。</param>
    /// <returns>沿初始方向移动的逻辑距离。</returns>
    public static double FragmentDistance(double time, double speed) => 12 *
        (time + ((0.5 * speed) - (speed * Math.Pow(2, (-10 * time) - 1))) * 1.4426950408889634);
    /// <summary>计算碎片在给定经过时间下的重力位移。</summary>
    /// <param name="time">碎片出生后经过的秒数。</param>
    /// <returns>重力引起的纵向位移。</returns>
    public static double FragmentGravity(double time) => 600 *
        (time - 0.954929658551372 * Math.Sin(1.0471975511965976 * time));
    /// <summary>计算指数缓出，区间两端分别返回 0 和 1。</summary>
    /// <param name="progress">插值进度。</param>
    /// <returns>指数缓出的插值系数。</returns>
    public static double OutExpo(double progress) => progress >= 1 ? 1 : progress <= 0 ? 0 : 1 - Math.Pow(2, -10 * progress);
    /// <summary>计算二次缓出进度。</summary>
    /// <param name="progress">插值进度。</param>
    /// <returns>二次缓出的插值系数。</returns>
    public static double OutQuad(double progress) => 1 - (1 - progress) * (1 - progress);
    /// <summary>依据星体年龄和开场标志计算可见透明度。</summary>
    /// <param name="star">被采样的星体。</param>
    /// <param name="time">采样时刻，单位为秒。</param>
    /// <returns>当前不透明度。</returns>
    public static double StarAlpha(Star star, double time) => star.Intro
        ? Math.Max(0, 2 - (time - star.Birth / 60.0) / 2)
        : Math.Max(0, Math.Min(1, 2 - (time - star.Birth / 60.0) / 2));
    /// <summary>根据缓存的累计运动量计算指定时刻的星体位移。</summary>
    /// <param name="star">被采样的星体。</param>
    /// <param name="time">采样时刻，单位为秒。</param>
    /// <returns>当前位置相对于出生位置的纵向位移。</returns>
    public double StarDisplacement(Star star, double time)
    {
        int tick = Math.Clamp((int)Math.Floor(time * 60 + 1e-8), 0, movementPrefix.Length - 1);
        if (tick < star.Birth) return 0;
        return star.Speed * (movementPrefix[tick] - movementPrefix[star.Birth - 1]);
    }
    /// <summary>查找指定时刻已生效的最近棋盘格状态。</summary>
    /// <param name="time">采样时刻，单位为秒。</param>
    /// <returns>最近生效的棋盘快照，无可用快照时为 null。</returns>
    public Checker? CheckerAt(double time)
    {
        if (Checkers.Count == 0) return null;
        int tick = Math.Max(0, (int)Math.Floor(time * 60 + 1e-8));
        int lo = 0, hi = Checkers.Count;
        while (lo < hi)
        {
            int mid = (lo + hi) / 2;
            if (Checkers[mid].Tick <= tick) lo = mid + 1;
            else hi = mid;
        }
        return lo == 0 ? null : Checkers[lo - 1];
    }
    void RefreshChecker(int tick)
    {
        // 棋盘为 40×22 格，概率判定使用大于或等于阈值。
        if (Checkers.Count >= 2048) return;
        var pixels = new bool[40 * 22];
        double threshold = Finite(timeline.Get("lasthours_checker_chance", tick / 60.0), 0);
        for (int x = 0; x < 40; x++)
            for (int y = 0; y < 22; y++) pixels[x + y * 40] = Real(0, 100) >= threshold;
        Checkers.Add(new(tick, pixels, nextCheckerRevision++));
    }
}
