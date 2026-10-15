namespace KuroakiGimmick.Core;

public static class GimmickLifecycle
{
    public static bool At(GimmickLifecycleDefinition? definition,IEnumerable<Timeline.Callback> events,double time)
    {
        if(definition==null)return true;
        bool alive=definition.Initial;
        foreach(var e in events)
        {
            if(e.Time>time)break;
            if(definition.Create.Contains(e.Name,StringComparer.Ordinal))alive=true;
            if(definition.Destroy.Contains(e.Name,StringComparer.Ordinal))alive=false;
        }
        return alive;
    }
}
