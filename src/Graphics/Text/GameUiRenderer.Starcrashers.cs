using System;
using System.Linq;
using KuroakiGimmick.Core;

namespace KuroakiGimmick.Graphics;

/// <summary>Starcrashers' source ss_starcrashers() ending, reusing the existing native textbox pipeline.</summary>
public sealed partial class GameUiRenderer
{
    private static readonly NativeSequenceDefinition StarcrashersEnding = CreateStarcrashersEnding();

    private static NativeSequenceDefinition CreateStarcrashersEnding()
    {
        var sequence = new NativeSequenceDefinition();
        const string trigger = "ending_coroutine";
        sequence.Story.Add(new(208.04, trigger, "Saturday", "Happy new year, sis."));
        sequence.Story.Add(new(211.29, trigger, "Saturday", "I'll see you again someday, okay?"));
        // The original text begins with `e{rcolor}, a GameMaker TextDrawer formatting command.
        // Strip the unsupported markup here rather than render its raw characters on screen.
        sequence.Story.Add(new(216.13, trigger, "", "...good night. I love you."));
        sequence.StoryWindows.Add(new(trigger, 208.04, 221.58, 222.58, 132, 1));
        return sequence;
    }

    public void DrawStarcrashersStory(Session session, double time)
    {
        if (session.Chart.ObjectName != "obj_starcrashers_gimmick" || time < 208.04 || time >= 222.58) return;
        // Unlike a normal mod track, ending_coroutine is a callback that starts ss_starcrashers().
        // Do not draw the story if the chart did not trigger it.
        if (!session.Chart.Mods.Any(e => e.Name == "ending_coroutine"
            && session.Timeline.Bpm.Time(e.Beat) <= time)) return;
        Use(session.GameUi);
        if (current?.Data == null) return;
        string font = Font(session);
        var state = NativeStoryState.Sample(StarcrashersEnding, time,
            _ => true, value => Width(font, value));
        DrawStoryBox(state, font);
    }
}
