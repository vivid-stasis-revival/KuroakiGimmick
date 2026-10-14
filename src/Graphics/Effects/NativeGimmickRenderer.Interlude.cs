using KuroakiGimmick.Core;

namespace KuroakiGimmick.Graphics;

public sealed partial class NativeGimmickRenderer
{
    public void Interlude(Session session, NativeInterludeState state)
    {
        Prepare(session);
        if (!state.Resumed)
        {
            canvas.Fill(new(0,0,320,180),Color.Hex(0));
            if (state.BackgroundSprite is { } background && SequenceTexture(session,background) is { } texture)
                canvas.Quad(texture,new(0,0,320,180),Color.White);
            foreach (var portrait in state.Portraits)
                DrawSequenceSprite(session,portrait.Sprite,portrait.Frame,portrait.X,portrait.Y,portrait.Scale*(portrait.Mirror?-1:1),
                    portrait.Scale,portrait.Alpha,state.SongTime,linear:true);
        }
        if (state.TintAlpha>0) canvas.Fill(new(0,0,320,180),Color.GameMaker(state.TintColorBgr).Alpha(state.TintAlpha));
        canvas.Flush();
    }
}
