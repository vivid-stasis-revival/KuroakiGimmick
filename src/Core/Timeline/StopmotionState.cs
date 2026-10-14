namespace KuroakiGimmick.Core;

/// <summary>Source instance lifecycles sampled from immutable chart events, including reverse seeks.</summary>
public static class StopmotionState
{
    public static bool IsCallback(string objectName,string name)=>objectName switch
    {
        "obj_stopmotion_gimmick"=>name is "storycheck1" or "storycheck2" or "temporaryshadercreate" or "temporaryshaderdestroy"
            or "sm_leftbg_frameset" or "sm_rightbg_frameset" or "sm_pushcommand" or "sm_killcommand",
        "obj_self_gimmick"=>name is "storycheck1" or "storycheck2" or "temporaryshadercreate" or "self_destsg" or "self_gravefg",
        _=>false
    };
    public static IEnumerable<ModEvent> Events(Chart chart, BpmMap bpm, double time) => chart.Mods
        .Where(e => bpm.Time(e.Beat) <= time).OrderBy(e => e.Beat).ThenBy(e => e.Order);

    public static bool ShaderActive(Chart chart, BpmMap bpm, double time)
    {
        bool active = true;
        foreach (var e in Events(chart, bpm, time))
        {
            if (e.Name == "temporaryshadercreate") active = true;
            if (e.Name == "storycheck2" || chart.ObjectName == "obj_stopmotion_gimmick" && e.Name == "temporaryshaderdestroy") active = false;
        }
        return active;
    }

    public static bool HasEvent(Chart chart, BpmMap bpm, double time, string name) => chart.Mods.Any(e => e.Name == name && bpm.Time(e.Beat) <= time);

    public sealed record BackgroundFrame(string Sprite,int Frame);
    public static BackgroundFrame Background(StopmotionDefinition definition, string initial, int initialFrame, string callback, Chart chart, BpmMap bpm, double time)
    {
        var last = Events(chart, bpm, time).LastOrDefault(e => e.Name == callback);
        if (last == null || definition.AreaBackgrounds.Length == 0) return new(initial,initialFrame);
        uint seed = unchecked((uint)last.Order * 65537u + (callback == "sm_leftbg_frameset" ? 3719u : 7193u));
        int index = Math.Min(definition.AreaBackgrounds.Length - 1, (int)(Timeline.Hash(seed) * definition.AreaBackgrounds.Length));
        return new(definition.AreaBackgrounds[index],definition.AreaFrames.Length==definition.AreaBackgrounds.Length?definition.AreaFrames[index]:0);
    }

    public sealed record Command(string Text, double Y);
    public static IReadOnlyList<Command> Commands(StopmotionDefinition definition, Chart chart, BpmMap bpm, double time)
    {
        var active = new List<int>();
        int current = 0;
        foreach (var e in Events(chart, bpm, time))
        {
            if (e.Name == "sm_killcommand") active.Clear();
            if (e.Name == "sm_pushcommand") active.Add(current++);
        }
        return active.Select((index, position) => new Command(index < definition.Commands.Length ? definition.Commands[index].ToUpperInvariant() : "",
            3 + (active.Count - position - 1) * 10)).Where(c => c.Y < 180).ToArray();
    }
}
