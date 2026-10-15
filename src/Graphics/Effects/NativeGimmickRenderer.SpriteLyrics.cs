using KuroakiGimmick.Core;

namespace KuroakiGimmick.Graphics;

public sealed partial class NativeGimmickRenderer
{
    private void DrawSpriteLyricsText(Session session, double time)
    {
        if (!session.NativeGimmick.HasBehavior("lyric-sprite")) return;
        var state = PersistentSpriteTextState.Sample(session.Timeline.Callbacks, time);
        if (state != null)
            DrawSequenceSprite(session, "sp_lastwish_text", state.Frame, 160, 90, 1, state.ScaleY, state.Alpha, time);
    }
}
