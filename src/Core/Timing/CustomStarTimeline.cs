namespace KuroakiGimmick.Core;

/// <summary>将宿主的时间轴查询与谱面诊断接入共享星体模拟。</summary>
public sealed class CustomStarTimeline : KenomaGMK.Runtime.Particles.CustomStarTimeline
{
    public CustomStarTimeline(Timeline timeline, Chart chart, double duration, bool enabled, bool sourceStars = false)
        : base((name,time)=>timeline.Get(name,time), message=>chart.Diagnostics.Add(new("stars",0,message,true)), duration, enabled, sourceStars) { }

    CustomStarTimeline(CustomStarTimeline source, Timeline owner)
        : base(source,(name,time)=>owner.Get(name,time)) { }

    internal CustomStarTimeline ForkForRender(Timeline owner) => new(this,owner);
}
