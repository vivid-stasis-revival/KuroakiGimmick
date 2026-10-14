namespace KuroakiGimmick.Core;

/// <summary>Source instance lifecycles sampled from immutable chart events, including reverse seeks.</summary>
public static class OperationConsoleState
{
    public static IEnumerable<Timeline.Callback> Events(Timeline timeline,double time)=>timeline.Callbacks.Where(e=>e.Time<=time);
    public static bool HasEvent(Timeline timeline,double time,string name)=>Events(timeline,time).Any(e=>e.Name==name);

    public sealed record BackgroundFrame(string Sprite,int Frame);
    public static BackgroundFrame Background(OperationConsoleDefinition definition, string initial, int initialFrame, string callback, Timeline timeline, double time, uint salt)
    {
        var last = Events(timeline,time).LastOrDefault(e => e.Name == callback);
        if (last == null || definition.AreaBackgrounds.Length == 0) return new(initial,initialFrame);
        uint seed = unchecked((uint)last.SourceOrder * 65537u + salt);
        int index = Math.Min(definition.AreaBackgrounds.Length - 1, (int)(Timeline.Hash(seed) * definition.AreaBackgrounds.Length));
        return new(definition.AreaBackgrounds[index],definition.AreaFrames.Length==definition.AreaBackgrounds.Length?definition.AreaFrames[index]:0);
    }

    public sealed record Command(string Text, double Y);
    public static IReadOnlyList<Command> Commands(OperationConsoleDefinition definition, Timeline timeline, double time)
    {
        var active = new List<int>();
        int current = 0;
        foreach (var e in Events(timeline,time))
        {
            if (e.Name == "sm_killcommand") active.Clear();
            if (e.Name == "sm_pushcommand") active.Add(current++);
        }
        return active.Select((index, position) => new Command(index < definition.Commands.Length ? definition.Commands[index].ToUpperInvariant() : "",
            3 + (active.Count - position - 1) * 10)).Where(c => c.Y < 180).ToArray();
    }
}
