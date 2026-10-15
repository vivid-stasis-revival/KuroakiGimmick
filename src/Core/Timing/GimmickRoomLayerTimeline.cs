namespace KuroakiGimmick.Core;

/// <summary>
/// GameMaker room background state. Alpha is taken from the chart's mod timeline,
/// while position is derived from absolute song time so arbitrary seeks reproduce
/// the same result and high-refresh-rate rendering does not change motion speed.
/// </summary>
public static class GimmickRoomLayerTimeline
{
    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Timeline, Dictionary<string, List<double>>> animationFrames = new();

    /// <summary>Integrate source FPS controls on a fixed 60 Hz grid so speed edits and reverse seeks cannot reset animation.</summary>
    public static double IntegratedFrames(Timeline timeline, string mod, double time)
    {
        if (!double.IsFinite(time) || time <= 0) return 0;
        time = Math.Min(time, 3600);
        var cache = animationFrames.GetOrCreateValue(timeline);
        lock (cache)
        {
            if (!cache.TryGetValue(mod, out var frames)) cache[mod] = frames = [0];
            int tick = (int)Math.Floor(time * 60);
            while (frames.Count <= tick)
            {
                int i = frames.Count - 1;
                double speed = timeline.Get(mod, (i + .5) / 60);
                frames.Add(frames[^1] + (double.IsFinite(speed) ? speed / 60 : 0));
            }
            double remainder = time - tick / 60.0;
            double value = timeline.Get(mod, tick / 60.0 + remainder / 2);
            return frames[tick] + (double.IsFinite(value) ? value * remainder : 0);
        }
    }
    public static double Alpha(GimmickRoomLayer layer, Timeline timeline, double time)
    {
        if (layer.VisibleMod is { } visibleMod)
        {
            double visible = timeline.Get(visibleMod, time);
            if (!double.IsFinite(visible) || visible < .5) return 0;
        }
        else if (!layer.Visible) return 0;
        double mod = layer.AlphaMod == null ? 1 : timeline.Get(layer.AlphaMod, time);
        if (layer.Bindings.TryGetValue("alpha", out var binding)) mod *= binding.Value(timeline, time);
        if (!double.IsFinite(mod) || !double.IsFinite(layer.Alpha)) return 0;
        return Math.Clamp(mod * layer.Alpha, 0, 1);
    }

    public static double X(GimmickRoomLayer layer, double time) =>
        layer.OffsetX + Math.Max(0, time) * layer.ScrollX;

    public static double Y(GimmickRoomLayer layer, double time) =>
        layer.OffsetY + Math.Max(0, time) * layer.ScrollY;

    public static double X(GimmickRoomLayer layer, Timeline timeline, double time) =>
        layer.Bindings.TryGetValue("x", out var binding) ? binding.Value(timeline, time) : X(layer, time);

    public static double Y(GimmickRoomLayer layer, Timeline timeline, double time) =>
        layer.Bindings.TryGetValue("y", out var binding) ? binding.Value(timeline, time) : Y(layer, time);

    public static int Frame(GimmickRoomLayer layer, Timeline timeline, double time, int count)
    {
        double frame = layer.Bindings.TryGetValue("frame", out var binding) ? binding.Value(timeline, time)
            : layer.FrameMod is { } mod ? timeline.Get(mod, time)
            : layer.FrameSpeedMod is { } speedMod ? IntegratedFrames(timeline, speedMod, time)
            : Math.Max(0, time) * layer.FramesPerSecond;
        return count <= 0 || !double.IsFinite(frame) ? 0 : (int)((Math.Floor(frame) % count + count) % count);
    }

    public static double Wrap(double position, double size)
    {
        if (size <= 0 || !double.IsFinite(position)) return 0;
        double value = position % size;
        return value < 0 ? value + size : value;
    }
}
