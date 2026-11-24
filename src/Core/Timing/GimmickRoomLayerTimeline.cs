// 在歌曲时刻求取房间层的滚动位置、可见性和动画帧。

namespace KuroakiGimmick.Core;

/// <summary>房间背景从 mod 时间轴采样透明度，并按歌曲绝对时间计算位置，使拖动和高刷新率绘制得到相同的运动轨迹。</summary>
public static class GimmickRoomLayerTimeline
{
    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Timeline, Dictionary<string, List<double>>> animationFrames = new();

    /// <summary>在固定 60 Hz 网格上积分帧率轨道，变速或倒拖时保持累计动画位置。</summary>
    /// <param name="timeline">用于采样轨道和回调的时间轴。</param>
    /// <param name="mod">要采样的 mod 名称。</param>
    /// <param name="time">采样时刻，单位为秒。</param>
    /// <returns>截至该时刻按帧率轨道累积的帧数。</returns>
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
    /// <summary>先判断图层可见性，再将轨道、绑定与基础不透明度相乘并限制在 0 至 1。</summary>
    /// <param name="layer">待采样的原版房间图层。</param>
    /// <param name="timeline">用于采样轨道和回调的时间轴。</param>
    /// <param name="time">采样时刻，单位为秒。</param>
    /// <returns>限制在 0 至 1 的不透明度，不可见时为 0。</returns>
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

    /// <summary>取得该时刻的横向位置，基础运动使用初始偏移和每秒滚动量。</summary>
    /// <param name="layer">待采样的原版房间图层。</param>
    /// <param name="time">采样时刻，单位为秒。</param>
    /// <returns>图层逻辑横坐标。</returns>
    public static double X(GimmickRoomLayer layer, double time) =>
        layer.OffsetX + Math.Max(0, time) * layer.ScrollX;

    /// <summary>取得该时刻的纵向位置，基础运动使用初始偏移和每秒滚动量。</summary>
    /// <param name="layer">待采样的原版房间图层。</param>
    /// <param name="time">采样时刻，单位为秒。</param>
    /// <returns>图层逻辑纵坐标。</returns>
    public static double Y(GimmickRoomLayer layer, double time) =>
        layer.OffsetY + Math.Max(0, time) * layer.ScrollY;

    /// <summary>取得该时刻的横向位置，基础运动使用初始偏移和每秒滚动量。</summary>
    /// <param name="layer">待采样的原版房间图层。</param>
    /// <param name="timeline">用于采样轨道和回调的时间轴。</param>
    /// <param name="time">采样时刻，单位为秒。</param>
    /// <returns>应用可选表达式绑定后的逻辑横坐标。</returns>
    public static double X(GimmickRoomLayer layer, Timeline timeline, double time) =>
        layer.Bindings.TryGetValue("x", out var binding) ? binding.Value(timeline, time) : X(layer, time);

    /// <summary>取得该时刻的纵向位置，基础运动使用初始偏移和每秒滚动量。</summary>
    /// <param name="layer">待采样的原版房间图层。</param>
    /// <param name="timeline">用于采样轨道和回调的时间轴。</param>
    /// <param name="time">采样时刻，单位为秒。</param>
    /// <returns>应用可选表达式绑定后的逻辑纵坐标。</returns>
    public static double Y(GimmickRoomLayer layer, Timeline timeline, double time) =>
        layer.Bindings.TryGetValue("y", out var binding) ? binding.Value(timeline, time) : Y(layer, time);

    /// <summary>依次按表达式、帧号轨道、帧速轨道或固定帧率选帧，并按帧数循环。</summary>
    /// <param name="layer">待采样的原版房间图层。</param>
    /// <param name="timeline">用于采样轨道和回调的时间轴。</param>
    /// <param name="time">采样时刻，单位为秒。</param>
    /// <param name="count">可用精灵帧数。</param>
    /// <returns>循环后的帧索引，无帧或计算值无效时为 0。</returns>
    public static int Frame(GimmickRoomLayer layer, Timeline timeline, double time, int count)
    {
        double frame = layer.Bindings.TryGetValue("frame", out var binding) ? binding.Value(timeline, time)
            : layer.FrameMod is { } mod ? timeline.Get(mod, time)
            : layer.FrameSpeedMod is { } speedMod ? IntegratedFrames(timeline, speedMod, time)
            : Math.Max(0, time) * layer.FramesPerSecond;
        return count <= 0 || !double.IsFinite(frame) ? 0 : (int)((Math.Floor(frame) % count + count) % count);
    }

    /// <summary>将坐标绕回一个铺设周期内，尺寸非正或坐标非有限时返回 0。</summary>
    /// <param name="position">待回绕的位置。</param>
    /// <param name="size">铺设周期。</param>
    /// <returns>周期内的位置，周期非正或位置非有限时为 0。</returns>
    public static double Wrap(double position, double size)
    {
        if (size <= 0 || !double.IsFinite(position)) return 0;
        double value = position % size;
        return value < 0 ? value + size : value;
    }
}
