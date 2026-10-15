using KuroakiGimmick.Core;

namespace KuroakiGimmick.Graphics;

public sealed partial class GameUiRenderer
{
    public void Interlude(Session session, NativeInterludeState state)
    {
        if (state.Resumed) return;
        Use(session.GameUi);
        if (current?.Data == null) return;
        string font=Font(session);
        DrawStoryBox(NativeStoryState.Sample(state.Sequence,state.Age,_=>true,text=>Width(font,text),session.Project.NativeStoryContext),font);
    }
}
