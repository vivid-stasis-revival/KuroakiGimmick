using KuroakiGimmick.Core;

namespace KuroakiGimmick.Graphics;

public sealed partial class NativeGimmickRenderer
{
    Timeline? sceneStoryTimeline;
    double sceneStoryTime = double.NaN;
    NativeInterludeState? sceneStoryState;
    private NativeInterludeState? SceneStory(Session session, double time)
    {
        Prepare(session);
        if (current?.Data?.Sequence is not { SceneActions.Count: > 0 } sequence) return null;
        if (ReferenceEquals(sceneStoryTimeline, session.Timeline) && sceneStoryTime == time) return sceneStoryState;
        sceneStoryTimeline = session.Timeline;
        sceneStoryTime = time;
        sceneStoryState = null;
        var events = sequence.SceneActions.Select(x => (Time: Math.Max(x.Time, session.Timeline.SequenceTriggerTime(x.Trigger)), x.Action))
            .Where(x => double.IsFinite(x.Time) && x.Time <= time).OrderBy(x => x.Time).ToArray();
        if (events.Length == 0) return null;
        var definition = new NativeInterludeDefinition { Trigger = "native-scene" };
        double cursor = 0;
        foreach (var item in events)
        {
            if (item.Time > cursor) definition.Steps.Add(new() { Duration = item.Time - cursor });
            definition.Steps.Add(new() { Duration = 0, Actions = [item.Action] });
            cursor = item.Time;
        }
        return sceneStoryState = new NativeInterludeSchedule(definition).Sample(time, time, keepVisible: true);
    }

    /// <summary>o_bg uses ordinary Draw, before the object's GUI-Begin post pass.</summary>
    public void DrawSequenceSceneBackground(Session session, double time)
    {
        if (SceneStory(session, time)?.BackgroundSprite is not { } sprite || SequenceTexture(session, sprite) is not { } image) return;
        canvas.Quad(image, new(0, 0, 320, 180), Color.White);
        canvas.Flush();
    }

    /// <summary>Story portraits and story tint use Draw GUI, before the textbox's smaller depth.</summary>
    public void DrawSequenceSceneGui(Session session, double time)
    {
        if (SceneStory(session, time) is not { } state) return;
        foreach (var portrait in state.Portraits)
            DrawSequenceSprite(session, portrait.Sprite, portrait.Frame, portrait.X, portrait.Y,
                portrait.Scale * (portrait.Mirror ? -1 : 1), portrait.Scale, portrait.Alpha, time, linear: true);
        if (state.TintAlpha > 0) canvas.Fill(new(0, 0, 320, 180), Color.GameMaker(state.TintColorBgr).Alpha(state.TintAlpha));
        canvas.Flush();
    }
}
