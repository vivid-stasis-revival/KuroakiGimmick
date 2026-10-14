namespace KuroakiGimmick.Core;

/// <summary>Source Step polling/reset semantics: a live tween can reassert a positive control every tick.</summary>
public sealed class InputInstructionState
{
    public sealed record Change(double Time, double? BlipBirth);
    public List<Change> Changes { get; } = [];
    public double CoverDestroyedAt { get; private set; } = double.PositiveInfinity;

    public InputInstructionState(Timeline timeline, double duration)
    {
        var spawn = timeline.Tracks.GetValueOrDefault(("sg_endblip", -1)) ?? [];
        var destroy = timeline.Tracks.GetValueOrDefault(("sg_endblip_destroy", -1)) ?? [];
        int spawnIndex = -1, destroyIndex = -1;
        double? birth = null;
        bool PositiveWrite(List<Timeline.Segment> track, double time, ref int cursor)
        {
            int before = cursor;
            while (cursor + 1 < track.Count && track[cursor + 1].Time <= time) cursor++;
            if (cursor < 0) return false;
            var value = track[cursor];
            bool written = before != cursor || value.Duration > 0 && time <= value.Time + value.Duration;
            return written && value.Value(time) > 0;
        }
        for (int tick = 0; tick <= Math.Ceiling(Math.Clamp(duration, 0, 3600) * 60); tick++)
        {
            double time = tick / 60.0;
            if (double.IsPositiveInfinity(CoverDestroyedAt) && timeline.Get("cover1", time) > 0) CoverDestroyedAt = time;
            bool spawned = PositiveWrite(spawn, time, ref spawnIndex);
            bool destroyed = PositiveWrite(destroy, time, ref destroyIndex);
            if (spawned) birth = time;
            if (destroyed) birth = null; // Source Step executes destroy after spawn, irrespective of chart declaration order.
            if (spawned || destroyed) Changes.Add(new(time, birth));
        }
    }
    public double? BlipBirthAt(double time)
    {
        int index = SortedSearch.UpperBound(Changes, time, c => c.Time) - 1;
        return index < 0 ? null : Changes[index].BlipBirth;
    }
}
