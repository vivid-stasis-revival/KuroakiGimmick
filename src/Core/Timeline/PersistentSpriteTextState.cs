namespace KuroakiGimmick.Core;

/// <summary>重放 lwtext 回调：替换当前实例，取值为 6 时淡出已有实例。</summary>
public static class PersistentSpriteTextState
{
    /// <summary>持久精灵文字的采样状态。</summary>
    /// <param name="Frame">精灵帧号。</param>
    /// <param name="ScaleY">垂直缩放倍率。</param>
    /// <param name="Alpha">不透明度。</param>
    public sealed record Pose(int Frame, double ScaleY, double Alpha);

    /// <summary>依据当前时间之前的回调返回精灵文字的帧、缩放与透明度。</summary>
    /// <param name="callbacks">按时刻排列的文字回调。</param>
    /// <param name="time">采样时刻，单位为秒。</param>
    /// <returns>精灵文字姿态，无活动实例时为 null。</returns>
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
