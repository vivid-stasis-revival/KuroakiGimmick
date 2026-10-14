using KuroakiGimmick.Core;

namespace KuroakiGimmick.Graphics;

public sealed partial class NativeGimmickRenderer
{
    Session? tutorialOwner;
    TutorialState? tutorialState;
    TutorialState Tutorial(Session session)
    {
        if (!ReferenceEquals(tutorialOwner, session))
        { tutorialOwner = session; tutorialState = new(session.Timeline, session.Duration); }
        return tutorialState!;
    }
    public void DrawTutorialEndBlip(Session session, double time)
    {
        if (session.Chart.ObjectName != "obj_tutorial_gimmick") return;
        if (Tutorial(session).BlipBirthAt(time) is { } birth) DrawNativeEndBlip(session, time, birth);
    }
    public void DrawTutorialCover(Session session, double time)
    {
        if (session.Chart.ObjectName != "obj_tutorial_gimmick" || !session.Project.NativeTutorialCover || time >= Tutorial(session).CoverDestroyedAt) return;
        DrawSequenceSprite(session, "sp_tutorial_coverhideui", 0, 0, 0, 1, 1, 1, time);
    }
}
