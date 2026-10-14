using KuroakiGimmick.Core;

namespace KuroakiGimmick.Graphics;

public sealed partial class NativeGimmickRenderer
{
    private void DrawLastwishText(Session session, double time)
    {
        if (session.Chart.ObjectName != "obj_lastwish_gimmick") return;
        var state = LastwishTextState.Sample(session.Timeline.Callbacks, time);
        if (state != null)
            DrawSequenceSprite(session, "sp_lastwish_text", state.Frame, 160, 90, 1, state.ScaleY, state.Alpha, time);
    }
}
