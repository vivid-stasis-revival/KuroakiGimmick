using System.Runtime.CompilerServices;

namespace KuroakiGimmick.Core;

/// <summary>Source lane_sprites latch, including its last_drawn_alpha cache and missing zero-reset branch.</summary>
public sealed class PartialSkinBlendState
{
    public readonly record struct State(double Time, double Alpha, bool Active, bool Direct);
    static readonly ConditionalWeakTable<Timeline, PartialSkinBlendState> cache = new();
    readonly List<State> states = [new(double.MinValue, 0, false, false)];

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
                // changeskin replaces every lane table even if its ID is unchanged.
                // The overlay only reasserts itself when its quantized mark changes.
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
                continue; // The source does not update last_drawn_alpha on this branch.
            }
            int quantized = (int)Math.Round(value * 255, MidpointRounding.ToEven);
            if (quantized == cachedAlpha) continue;
            cachedAlpha = quantized;
            states.Add(current = new(time, value, true, false));
        }
    }

    public State Sample(double time) => states[Math.Max(0, SortedSearch.UpperBound(states, time, x => x.Time) - 1)];
    public static State At(Timeline timeline, double time) => cache.GetValue(timeline, key => new(key)).Sample(time);
}
