namespace KuroakiGimmick.Core;

/// <summary>Source Plaudite jacket latch, persistent red-particle state and directional bursts.</summary>
public sealed class PlauditeObjects
{
    public readonly record struct Jacket(double Time, int Index);
    public readonly record struct RedWrite(double Time, bool Front);
    public List<Jacket> Jackets { get; } = [new(double.MinValue, 0)];
    public List<RedWrite> RedWrites { get; } = [];
    public List<Timeline.Dust> Bursts { get; } = [];
    public double EndingTime { get; }

    public PlauditeObjects(Timeline timeline)
    {
        EndingTime = timeline.Callbacks.FirstOrDefault(e => e.Name == "plaudite_ending_event")?.Time ?? double.PositiveInfinity;
        int jacket = 0;
        for (int tick = 0; tick <= Math.Ceiling(Math.Min(3600, timeline.End) * 60); tick++)
        {
            double time = tick / 60.0, value = timeline.Get("plaudite_jacket", time);
            if (value is >= 0 and <= 10 && value == Math.Truncate(value) && (int)value != jacket)
                Jackets.Add(new(time, jacket = (int)value));
            if (RoomFxState.Enabled(timeline.Get("plaudite_red_particle", time)))
                RedWrites.Add(new(time, timeline.Get("plaudite_sect7bg", time) > .5));
        }
        foreach (var callback in timeline.Callbacks.Where(e => e.Name == "plaudite_pburst"))
        {
            double requested = Math.Ceiling(Math.Max(0, callback.Value));
            if (requested > 50_000 || Bursts.Count + requested * 2 > 100_000)
            { timeline.Native?.Fail("plaudite-pburst", "Directional particle count exceeds the 100000-instance preview budget."); continue; }
            // Source spawn_particles_directional uses <= amount/bgparticles,
            // so default high quality creates two particles for each outer loop.
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
                        { death = time; break; }
                    }
                    Bursts.Add(particle with { Death = death });
                }
            }
        }
        Bursts.Sort((a, b) => a.Time.CompareTo(b.Time));
    }

    public int JacketAt(double time) => Jackets[Math.Max(0, SortedSearch.UpperBound(Jackets, time, x => x.Time) - 1)].Index;
    public (bool Red, bool Front) ParticleState(double birth, double time)
    {
        int index = SortedSearch.UpperBound(RedWrites, time, x => x.Time) - 1;
        return index >= 0 && RedWrites[index].Time >= birth ? (true, RedWrites[index].Front) : (false, false);
    }
}
