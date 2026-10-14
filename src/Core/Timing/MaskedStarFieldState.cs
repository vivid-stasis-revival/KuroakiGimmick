using System;
using System.Collections.Generic;
using System.Linq;

namespace KuroakiGimmick.Core;

/// <summary>
/// Starcrashers's o_sc_* object simulation, reconstructed from v6.2.0.1 GML.
/// All steps run at fixed 60 Hz. Results are immutable after baking and independent of
/// preview FPS and seek direction. Random values are deterministic surrogates, NOT a
/// bit-identical emulation of GameMaker's unknown per-session RNG state.
/// </summary>
public sealed class MaskedStarFieldState
{
    public const int TicksPerSecond = 60;
    const int MaxTicks = 60 * 3600;
    const int MaxStars = 40000;
    const int MaxEffects = 30000;
    public sealed record Star(int Birth, int Death, double X, double Y, double Speed, bool Intro, double InitialAngle);
    public sealed record CenterRect(int Birth, bool Red);
    public sealed record Slash(int Birth, double TopX, double BottomX);
    public sealed record Firework(int Birth, int Burst, double X, double EndpointY);
    public sealed record Fragment(int Birth, int Death, double X, double Y, int Frame, double Direction, double Speed);
    public sealed record Checker(int Tick, bool[] Pixels, int Revision);
    public List<Star> Stars { get; } = [];
    public List<CenterRect> CenterRects { get; } = [];
    public List<Slash> Slashes { get; } = [];
    public List<Firework> Fireworks { get; } = [];
    public List<Fragment> Fragments { get; } = [];
    public List<Checker> Checkers { get; } = [];
    // All object lists are appended in chronological order. Binary-search a four-second
    // viewport instead of scanning a full song of already-dead objects every render frame.
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

    public MaskedStarFieldState(Session session) : this(session.Timeline, session.Chart, session.Duration) { }

    public MaskedStarFieldState(Timeline sourceTimeline, Chart chart, double duration)
    {
        timeline = sourceTimeline;
        int ticks = Math.Clamp((int)Math.Ceiling(Math.Max(0, duration) * TicksPerSecond), 0, MaxTicks);
        movementPrefix = new double[ticks + 1];
        // Death searches must see future movement, including later reversals.
        // Building this prefix lazily inside the spawn loop resurrects stars
        // which should already have crossed the source y>=185 destruction edge.
        for (int tick = 1; tick <= ticks; tick++)
            movementPrefix[tick] = movementPrefix[tick - 1] + Finite(timeline.Get("starspd_multiplier", tick / 60.0), 1);
        // Constant seed: stable seeking/export. GameMaker's session seed is not present in the extracted GML.
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
        // Checker surface is created on first Draw regardless of the mod initial value.
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
            { RefreshChecker(tick); checkerIndex++; }
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

    public static bool IsInstanceCallback(string name) => name is "spawn_star_mask" or "spawn_centerrect_red" or
        "spawn_centerrect_black" or "laser_spawn" or "chorus_firework";
    static double Finite(double v, double fallback) => double.IsFinite(v) ? v : fallback;
    // xorshift32 is intentionally an explicit surrogate, not a claim of GameMaker RNG equivalence.
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
            // Match Step_0 destruction after its first failing tick, not a continuous approximation.
            for (int t = tick; t < movementPrefix.Length; t++)
            {
                double age = (t - tick + 1) / 60.0;
                if (Math.Min(1, 2 - age / 2) <= 0 ||
                    y + speed * (movementPrefix[t] - movementPrefix[tick - 1]) >= 185)
                { death = t; break; }
            }
        }
        Stars.Add(new(tick, death, x, y, speed, intro, 0));
    }
    void IntroStar(int tick, double x, double y) => SpawnStar(tick, x, y, true);
    void FireworkSpawn(int birth, double x, double endpointY)
    {
        int burst = birth + (int)Math.Ceiling(.34 * 60);
        Fireworks.Add(new(birth, burst, x, endpointY));
        // o_sc_firework.Step_0 creates exactly 30 particles on the final tick.
        for (int i = 0; i < 30 && Fragments.Count < MaxEffects * 30; i++)
        {
            int frame = Int(0, 3);
            double angle = Real(0, 360);
            double speed = Real(2.8, 4.8);
            double y = endpointY; // draw position on spawn tick (lerp endpoint when timer >= 1)
            double direction = angle * Math.PI / 180;
            int death = movementPrefix.Length;
            // GML Step_0: lengthdir_y = -sin(degrees) * dist.
            for (int t = burst; t < movementPrefix.Length; t++)
            {
                double age = (t - burst) / 60.0;
                double py = y - Math.Sin(direction) * FragmentDistance(age, speed) + FragmentGravity(age);
                if (py >= 180) { death = t; break; }
            }
            Fragments.Add(new(burst, death, x, y, frame, direction, speed));
        }
    }
    public static double FragmentDistance(double time, double speed) => 12 *
        (time + ((0.5 * speed) - (speed * Math.Pow(2, (-10 * time) - 1))) * 1.4426950408889634);
    public static double FragmentGravity(double time) => 600 *
        (time - 0.954929658551372 * Math.Sin(1.0471975511965976 * time));
    public static double OutExpo(double progress) => progress >= 1 ? 1 : progress <= 0 ? 0 : 1 - Math.Pow(2, -10 * progress);
    public static double OutQuad(double progress) => 1 - (1 - progress) * (1 - progress);
    public static double StarAlpha(Star star, double time) => star.Intro
        ? Math.Max(0, 2 - (time - star.Birth / 60.0) / 2)
        : Math.Max(0, Math.Min(1, 2 - (time - star.Birth / 60.0) / 2));
    public double StarDisplacement(Star star, double time)
    {
        int tick = Math.Clamp((int)Math.Floor(time * 60 + 1e-8), 0, movementPrefix.Length - 1);
        if (tick < star.Birth) return 0;
        return star.Speed * (movementPrefix[tick] - movementPrefix[star.Birth - 1]);
    }
    public Checker? CheckerAt(double time)
    {
        if (Checkers.Count == 0) return null;
        int tick = Math.Max(0, (int)Math.Floor(time * 60 + 1e-8));
        int lo = 0, hi = Checkers.Count;
        while (lo < hi) { int mid = (lo + hi) / 2; if (Checkers[mid].Tick <= tick) lo = mid + 1; else hi = mid; }
        return lo == 0 ? null : Checkers[lo - 1];
    }
    void RefreshChecker(int tick)
    {
        // 40x22, probability direction is >= threshold (not <=).
        if (Checkers.Count >= 2048) return;
        var pixels = new bool[40 * 22];
        double threshold = Finite(timeline.Get("lasthours_checker_chance", tick / 60.0), 0);
        for (int x = 0; x < 40; x++)
            for (int y = 0; y < 22; y++) pixels[x + y * 40] = Real(0, 100) >= threshold;
        Checkers.Add(new(tick, pixels, nextCheckerRevision++));
    }
}
