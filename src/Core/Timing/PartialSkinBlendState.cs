using System.Runtime.CompilerServices;

namespace KuroakiGimmick.Core;

/// <summary>重建原版 lane_sprites 的锁存行为，包括 last_drawn_alpha 缓存与未在零值时复位的分支。</summary>
public sealed class PartialSkinBlendState
{
    /// <summary>一次皮肤混合状态变化。</summary>
    /// <param name="Time">变化生效的歌曲秒数。</param>
    /// <param name="Alpha">叠加皮肤的不透明度。</param>
    /// <param name="Active">是否启用叠加。</param>
    /// <param name="Direct">是否直接使用完整叠加皮肤。</param>
    public readonly record struct State(double Time, double Alpha, bool Active, bool Direct);
    static readonly ConditionalWeakTable<Timeline, PartialSkinBlendState> cache = new();
    readonly List<State> states = [new(double.MinValue, 0, false, false)];

    /// <summary>沿时间轴按固定 tick 重建轨道皮肤的混合状态变化。</summary>
    /// <param name="timeline">用于采样轨道和回调的时间轴。</param>
    public PartialSkinBlendState(Timeline timeline)
    {
        int cachedAlpha = 0;
        State current = states[0];
        int skinEvent = 0;
        var skinChanges = timeline.CustomSkinChangeEnabled
            && timeline.Tracks.TryGetValue(("changeskin", -1), out var changes) ? changes : null;
        for (int tick = 0; tick <= Math.Ceiling(Math.Min(3600, timeline.End) * 60); tick++)
        {
            double time = tick / 60.0, value = timeline.Get("chorus_greenleft_overlay", time);
            if (timeline.IsCustomGimmick)
            {
                // 即使皮肤 ID 未变，changeskin 仍替换所有轨道精灵表；仅当量化标志变化时才重新应用叠加层。
                while (skinChanges != null && skinEvent < skinChanges.Count && skinChanges[skinEvent].Time <= time)
                {
                    skinEvent++;
                    if (current.Active) states.Add(current = new(time, 0, false, false));
                }
                value = double.IsFinite(value) ? Math.Clamp(value, 0, 1) : 0;
                int mark = (int)Math.Round(value * 255, MidpointRounding.ToEven);
                if (mark != cachedAlpha)
                {
                    cachedAlpha = mark;
                    states.Add(current = new(time, value, mark > 0, mark >= 255));
                }
                continue;
            }
            if (!double.IsFinite(value) || value <= 0) continue;
            if (value >= 1)
            {
                if (!current.Direct) states.Add(current = new(time, 1, true, true));
                continue; // 该分支沿用原版行为，不更新 last_drawn_alpha。
            }
            int quantized = (int)Math.Round(value * 255, MidpointRounding.ToEven);
            if (quantized == cachedAlpha) continue;
            cachedAlpha = quantized;
            states.Add(current = new(time, value, true, false));
        }
    }

    /// <summary>查询当前时刻最后生效的皮肤状态。</summary>
    /// <param name="time">采样时刻，单位为秒。</param>
    /// <returns>最近一次已生效的混合状态。</returns>
    public State Sample(double time) => states[Math.Max(0, SortedSearch.UpperBound(states, time, x => x.Time) - 1)];
    /// <summary>在对应时间轴的缓存中查询指定时刻的皮肤状态。</summary>
    /// <param name="timeline">用于采样轨道和回调的时间轴。</param>
    /// <param name="time">采样时刻，单位为秒。</param>
    /// <returns>对应时间轴的皮肤混合状态。</returns>
    public static State At(Timeline timeline, double time) => cache.GetValue(timeline, key => new(key)).Sample(time);
}
