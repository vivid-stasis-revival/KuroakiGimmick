using KuroakiGimmick.Core;

namespace KuroakiGimmick.Graphics;

public sealed partial class NativeGimmickRenderer
{
    Session? tutorialOwner;
    InputInstructionState? tutorialState;
    InputInstructionState Tutorial(Session session)
    {
        if (!ReferenceEquals(tutorialOwner, session))
        { tutorialOwner = session; tutorialState = new(session.Timeline, session.Duration); }
        return tutorialState!;
    }
    public void DrawInputInstructionsEndBlip(Session session, double time)
    {
        if (!session.NativeGimmick.HasBehavior("instruction-panel")) return;
        if (Tutorial(session).BlipBirthAt(time) is { } birth) DrawNativeEndBlip(session, time, birth);
    }
    public void DrawInputInstructionsCover(Session session, double time)
    {
        if (!session.NativeGimmick.HasBehavior("instruction-panel") || !session.Project.NativeTutorialCover || time >= Tutorial(session).CoverDestroyedAt) return;
        DrawSequenceSprite(session, "sp_tutorial_coverhideui", 0, 0, 0, 1, 1, 1, time);
    }
}
