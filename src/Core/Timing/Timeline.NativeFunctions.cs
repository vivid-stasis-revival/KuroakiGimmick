namespace KuroakiGimmick.Core;

public sealed partial class Timeline
{
    NativeFunctionTimeline? nativeFunctions;
    internal double NativeRawValue(string name,double time,int proxy=-1) =>
        Tracks.TryGetValue((name,proxy),out var list)?Evaluate(list,time,Initial(name,proxy)):Initial(name,proxy);

    internal double NativeTrackWriteTime(string name,int proxy,double time)
    {
        if(!Tracks.TryGetValue((name,proxy),out var list))return double.NegativeInfinity;
        int index=SortedSearch.UpperBound(list,time,static x=>x.Time)-1;
        if(index<0)return double.NegativeInfinity;
        var item=list[index];
        double end=Math.Max(item.Time,item.Time+item.Duration);
        return Math.Min(time,Math.Ceiling(end*60-1e-8)/60);
    }
}
