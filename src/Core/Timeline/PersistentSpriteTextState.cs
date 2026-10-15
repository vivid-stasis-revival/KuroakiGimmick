namespace KuroakiGimmick.Core;

/// <summary>Source lwtext callback: replace the instance, or fade the live instance for value 6.</summary>
public static class PersistentSpriteTextState
{
    public sealed record Pose(int Frame, double ScaleY, double Alpha);

    public static Pose? Sample(IReadOnlyList<Timeline.Callback> callbacks, double time)
    {
        double? born = null, fade = null;
        double frame = 0;
        foreach (var e in callbacks)
        {
            if (e.Time > time) break;
            if (e.Name != "lwtext") continue;
            if (fade is { } expired && e.Time >= expired + 1) born = null;
            if (e.Value == 6)
            {
                if (born != null) fade = e.Time;
            }
            else
            {
                born = e.Time;
                frame = e.Value - 1;
                fade = null;
            }
        }
        if (born == null || fade is { } ended && time >= ended + 1 || !double.IsFinite(frame)) return null;
        double progress = Math.Clamp((time - born.Value) * 3, 0, 1);
        double scale = 1 + .25 * (progress == 1 ? 0 : Math.Pow(2, -10 * progress));
        return new((int)((Math.Floor(frame) % 5 + 5) % 5), scale, fade is { } start ? Math.Clamp(1 - time + start, 0, 1) : 1);
    }
}
