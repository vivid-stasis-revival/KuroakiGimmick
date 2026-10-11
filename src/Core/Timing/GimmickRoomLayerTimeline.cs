namespace KuroakiGimmick.Core;

/// <summary>
/// GameMaker room background state. Alpha is taken from the chart's mod timeline,
/// while position is derived from absolute song time so arbitrary seeks reproduce
/// the same result and high-refresh-rate rendering does not change motion speed.
/// </summary>
public static class GimmickRoomLayerTimeline
{
    public static double Alpha(GimmickRoomLayer layer, Timeline timeline, double time)
    {
        if (!layer.Visible) return 0;
        double mod = layer.AlphaMod == null ? 1 : timeline.Get(layer.AlphaMod, time);
        if (!double.IsFinite(mod) || !double.IsFinite(layer.Alpha)) return 0;
        return Math.Clamp(mod * layer.Alpha, 0, 1);
    }

    public static double X(GimmickRoomLayer layer, double time) =>
        layer.OffsetX + Math.Max(0, time) * layer.ScrollX;

    public static double Y(GimmickRoomLayer layer, double time) =>
        layer.OffsetY + Math.Max(0, time) * layer.ScrollY;

    public static double Wrap(double position, double size)
    {
        if (size <= 0 || !double.IsFinite(position)) return 0;
        double value = position % size;
        return value < 0 ? value + size : value;
    }
}
