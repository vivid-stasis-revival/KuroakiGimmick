namespace KuroakiGimmick.Core;

/// <summary>原版 NoteScoring 在成功命中时将震动设为 1，Step 每秒衰减一个单位。</summary>
public static class ExtendnovaHudState
{
    /// <summary>依据判定与震动轨道计算当前 HUD 的横纵偏移。</summary>
    /// <param name="timeline">用于采样轨道和回调的时间轴。</param>
    /// <param name="score">自动演奏判定与计分状态。</param>
    /// <param name="time">采样时刻，单位为秒。</param>
    /// <param name="mod">要采样的 mod 名称。</param>
    /// <returns>当前时刻的 HUD 横纵偏移。</returns>
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
