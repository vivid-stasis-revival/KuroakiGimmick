using KuroakiGimmick.Core;

namespace KuroakiGimmick.Graphics;

public sealed partial class NativeGimmickRenderer
{
    Session? multigrodeOwner;
    MultigrodeState? multigrodeState;
    MultigrodeState Multigrode(Session session)
    {
        if (!ReferenceEquals(multigrodeOwner, session))
        { multigrodeOwner = session; multigrodeState = new(session.Timeline, session.Chart, session.NativeGimmick.Data!.DialogueTable.Count); }
        return multigrodeState!;
    }
    public void DrawMultigrodeDialogue(Session session, double time, GameUiRenderer ui)
    {
        if (session.Chart.ObjectName != "obj_multigrode_gimmick" || session.NativeGimmick.Data == null) return;
        DrawSequenceSprite(session, "sp_af2024_multi_dialogue", 0, 160, 78, 1, session.Timeline.Get("dialogue_size", time), 1, time);
        ui.DrawMultigrodeSender(session, time);
    }
    public void DrawMultigrodeGui(Session session, double time, GameUiRenderer ui)
    {
        if (session.Chart.ObjectName != "obj_multigrode_gimmick" || session.NativeGimmick.Data == null) return;
        var state = Multigrode(session);
        DrawSequenceSprite(session, "sp_af2024_fruitmemory", 0, session.Timeline.Get("fruitmemoryx", time), 35, 1, 1, 1, time);
        ui.DrawMultigrodeGui(session, state, time);
    }
}
