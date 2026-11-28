using System.Runtime.CompilerServices;

namespace KuroakiGimmick.Core;

/// <summary>按宿主 Timeline 身份缓存共享采样器。</summary>
public sealed class PartialSkinBlendState : KenomaGMK.Runtime.Skins.PartialSkinBlendState
{
    static readonly ConditionalWeakTable<Timeline, PartialSkinBlendState> cache = new();
    public PartialSkinBlendState(Timeline timeline)
        : base(timeline.End,timeline.IsCustomGimmick,
            timeline.CustomSkinChangeEnabled && timeline.Tracks.TryGetValue(("changeskin",-1),out var changes)
                ? changes.Select(s=>s.Time) : [],
            time=>timeline.Get("chorus_greenleft_overlay",time)) { }

    public static State At(Timeline timeline,double time)=>cache.GetValue(timeline,key=>new(key)).Sample(time);
}
