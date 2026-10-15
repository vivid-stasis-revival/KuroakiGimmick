namespace KuroakiGimmick.Core;

/// <summary>Source o_sg_star state, baked at 60 Hz with seek-independent editor random seeds.</summary>
public sealed class SideStarFieldState
{
    public sealed record Position(double Time, double X, double Y, double Travel);
    public sealed class Star
    {
        public double Birth { get; init; }
        public double Death { get; set; } = double.PositiveInfinity;
        public double Speed { get; init; }
        public int Frame { get; init; }
        public int Side { get; init; }
        public bool FromSpawner { get; init; }
        public double Hue { get; init; }
        public double BirthTravel { get; init; }
        public List<Position> Positions { get; } = [];
    }
    public sealed record Pose(double X, double Y, double Angle, double Alpha, double Hue, int Frame, bool FromSpawner);
    public List<Star> Stars { get; } = [];
    readonly Timeline timeline;
    readonly double[] travel;
    public SideStarFieldState(Timeline timeline, Chart chart, double duration)
    {
        this.timeline = timeline;
        int ticks = (int)Math.Ceiling(Math.Clamp(duration, 0, 3600) * 60);
        travel = new double[ticks + 1];
        var alive = new List<Star>();
        var events = timeline.Callbacks.Where(e => e.Name is "sides" or "starspawn").ToArray();
        int cursor = 0; uint seed = 27171; double timer = 0;
        double Random() => Timeline.Hash(unchecked(seed++));
        int Integer(int min, int max) => min + Math.Min(max - min, (int)(Random() * (max - min + 1)));
        void Spawn(double time, int tick, int side, bool automatic)
        {
            if (Stars.Count >= 100000) return;
            double x = Integer(0, 110) + 210 * side;
            double y = automatic ? Integer(-100, 80) : Integer(-10, 90);
            // Automatic spawning assigns l AFTER Create; the Create hue sees its default 0.
            double hue = ((x * .6 + 180 - 62 * (automatic ? 0 : side)) * .7) / 255;
            double low = timeline.Get("starspd_low", time), high = timeline.Get("starspd_high", time);
            var star = new Star { Birth = time, Frame = Integer(0, 3), Side = side, FromSpawner = automatic,
                Hue = hue, Speed = low + (high - low) * Random(), BirthTravel = travel[tick] };
            star.Positions.Add(new(time, x, y, travel[tick]));
            Stars.Add(star); alive.Add(star);
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
                    { chart.Diagnostics.Add(new("native-stargazers", 0, "Star callback exceeds the source-preview particle budget.", true)); continue; }
                    for (int pair = 0; pair < count; pair++) { Spawn(time, tick, 0, false); Spawn(time, tick, 1, false); }
                }
            }
            for (int i = alive.Count - 1; i >= 0; i--)
            {
                var star = alive[i]; var at = star.Positions[^1];
                if (time - star.Birth >= 4 || at.Y + star.Speed * (travel[tick] - at.Travel) >= 185)
                { star.Death = time; alive.RemoveAt(i); }
            }
            double interval = timeline.Get("sg_starspawner", time);
            if (tick > 0 && RoomFxState.Enabled(pause) && double.IsFinite(interval) && interval > 0)
            {
                timer--;
                while (timer <= 0 && Stars.Count < 100000)
                { Spawn(time, tick, Integer(0, 1), true); timer += interval; }
            }
        }
        if (Stars.Count >= 100000) chart.Diagnostics.Add(new("native-stargazers", 0, "Star simulation exceeds 100000 particles.", true));
    }
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
    public static double? EndBlipBirth(Timeline timeline, double time) => timeline.Callbacks
        .LastOrDefault(e => e.Name == "sg_endblip" && e.Time <= time)?.Time;
}
