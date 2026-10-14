namespace KuroakiGimmick.Core;

/// <summary>Source NoteScoring sets shake=1 on successful hits; Step decays it at one unit/second.</summary>
public static class ExtendnovaHudState
{
    public static (double X, double Y) Shake(Timeline timeline, ScoreState score, double time, string mod)
    {
        double strength = 0;
        foreach (var hit in score.HitsBetween(time - 1, time))
            if (hit.Kind != ScoreState.HitKind.Mine && RoomFxState.Enabled(timeline.Get(mod, hit.Time)))
                strength = Math.Max(strength, Math.Max(0, 1 - time + hit.Time));
        uint tick = unchecked((uint)Math.Max(0, Math.Floor(time * 60)));
        double angle = Timeline.Hash(tick * 8171 + 12983) * Math.PI * 2;
        return (strength * Math.Cos(angle), -strength * Math.Sin(angle));
    }
}
