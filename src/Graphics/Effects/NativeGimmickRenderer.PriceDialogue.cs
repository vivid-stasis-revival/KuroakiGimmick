using KuroakiGimmick.Core;

namespace KuroakiGimmick.Graphics;

public sealed partial class NativeGimmickRenderer
{
    Session? multigrodeOwner;
    PriceDialogueState? multigrodeState;
    PriceDialogueState Multigrode(Session session)
    {
        if (!ReferenceEquals(multigrodeOwner, session))
        { multigrodeOwner = session; multigrodeState = new(session.Timeline, session.Chart, session.NativeGimmick.Data!.DialogueTable.Count); }
        return multigrodeState!;
    }
    public void DrawPriceDialogueDialogue(Session session, double time, GameUiRenderer ui)
    {
        if (!session.NativeGimmick.HasBehavior("price-dialogue") || session.NativeGimmick.Data == null) return;
        DrawSequenceSprite(session, "sp_af2024_multi_dialogue", 0, 160, 78, 1, session.Timeline.Get("dialogue_size", time), 1, time);
        ui.DrawPriceDialogueSender(session, time);
    }
    public void DrawPriceDialogueGui(Session session, double time, GameUiRenderer ui)
    {
        if (!session.NativeGimmick.HasBehavior("price-dialogue") || session.NativeGimmick.Data == null) return;
        var state = Multigrode(session);
        DrawSequenceSprite(session, "sp_af2024_fruitmemory", 0, session.Timeline.Get("fruitmemoryx", time), 35, 1, 1, 1, time);
        ui.DrawPriceDialogueGui(session, state, time);
    }
}
