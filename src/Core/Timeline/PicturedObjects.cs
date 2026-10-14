namespace KuroakiGimmick.Core;

/// <summary>Seekable replay of Pictured's self-resetting controls and persistent cube generator.</summary>
public sealed class PicturedObjects
{
    public readonly record struct Picture(double Time, int Frame, bool Visible);
    public readonly record struct Cubes(double Time, int Count, uint Seed);
    public List<Picture> Pictures { get; } = [new(double.MinValue, 0, false)];
    public List<double> Pulses { get; } = [];
    public List<Cubes> CubeBatches { get; } = [];
    public double CubeDeath { get; private set; } = double.PositiveInfinity;

    public PicturedObjects(Timeline timeline)
    {
        int frame = 0, serial = 0;
        foreach (var (time, value) in Writes(timeline, "2_bg_pics"))
        {
            if (RoomFxState.Enabled(value))
                frame = Math.Min(26, (int)(Timeline.Hash(unchecked((uint)serial++ * 65537 + 9143)) * 27));
            // A consumed positive value resets to 0, which remains visible.
            Pictures.Add(new(time, frame, value >= 0));
        }
        foreach (var (time, value) in Writes(timeline, "2_dialbox_pulsar"))
            if (RoomFxState.Enabled(value)) Pulses.Add(time);
        int total = 0;
        int ticks = (int)Math.Ceiling(Math.Min(3600, timeline.End) * 60);
        for (int tick = 1; tick <= ticks; tick++)
        {
            double time = tick / 60.0, glitch = timeline.Get("1_glitch", time);
            if (glitch > 1)
            {
                // Both the generator and all its existing cubes destroy themselves.
                CubeDeath = time;
                break;
            }
            if (glitch <= 0) continue;
            if (total + 90 > 100_000)
            {
                timeline.Native?.Fail("pictured-cubes", "Persistent cube count exceeds the 100000-instance preview budget.");
                break;
            }
            CubeBatches.Add(new(time, 90, unchecked((uint)tick * 65537 + 3917)));
            total += 90;
        }
    }

    public Picture PictureAt(double time)
    {
        int index = SortedSearch.UpperBound(Pictures, time, x => x.Time) - 1;
        return index < 0 ? new(0, 0, false) : Pictures[index];
    }

    // Tween writes are sampled at the source logic clock. Once a completed write
    // has been consumed, its held endpoint cannot retrigger the reset control.
    public static IEnumerable<(double Time, double Value)> Writes(Timeline timeline, string name)
    {
        if (!timeline.Tracks.TryGetValue((name, -1), out var segments)) yield break;
        var writes = new SortedDictionary<double, double>();
        for (int i = 0; i < segments.Count; i++)
        {
            var segment = segments[i];
            double next = i + 1 < segments.Count ? segments[i + 1].Time : double.PositiveInfinity;
            double end = Math.Min(3600, Math.Min(segment.Time + segment.Duration, next));
            if (segment.Time >= next || segment.Time > 3600) continue;
            if (segment.Duration <= 0) { writes[segment.Time] = segment.To; continue; }
            for (int tick = (int)Math.Max(0, Math.Ceiling(segment.Time * 60)); tick <= Math.Floor(end * 60); tick++)
            {
                double time = tick / 60.0;
                if (time < next) writes[time] = segment.Value(time);
            }
            if (end < next && end == segment.Time + segment.Duration) writes[end] = segment.To;
        }
        foreach (var pair in writes) yield return (pair.Key, pair.Value);
    }
}
