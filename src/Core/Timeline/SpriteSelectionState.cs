namespace KuroakiGimmick.Core;

/// <summary>Retain the last valid discrete sprite selection across tweens and seeks.</summary>
public sealed class SpriteSelectionState
{
    readonly List<(double Time,int Index)> choices=[(double.NegativeInfinity,0)];
    public SpriteSelectionState(Timeline timeline,string mod,int count)
    {
        int index=0;
        for(int tick=0;tick<=Math.Ceiling(Math.Min(3600,timeline.End)*60);tick++)
        {
            double time=tick/60.0,value=timeline.Get(mod,time);
            if(value>=0&&value<count&&value==Math.Truncate(value)&&(int)value!=index)choices.Add((time,index=(int)value));
        }
    }
    public int At(double time)=>choices[Math.Max(0,SortedSearch.UpperBound(choices,time,c=>c.Time)-1)].Index;
}
