// 绘制原版演出插段。

using KuroakiGimmick.Core;

namespace KuroakiGimmick.Graphics;

public sealed partial class NativeGimmickRenderer
{
    /// <summary>将插段采样状态中的背景、立绘及着色绘制到场景。</summary>
    /// <param name="session">提供谱面、时间轴及已加载资源的会话。</param>
    /// <param name="state">当前插段的采样状态。</param>
    public void Interlude(Session session, NativeInterludeState state)
    {
        Prepare(session);
        if (!state.Resumed)
        {
            canvas.Fill(new(0, 0, 320, 180), Color.Hex(0));
            if (state.BackgroundSprite is { } background && SequenceTexture(session, background) is { } texture)
                canvas.Quad(texture, new(0, 0, 320, 180), Color.White);
            foreach (var portrait in state.Portraits)
                DrawSequenceSprite(session, portrait.Sprite, portrait.Frame, portrait.X, portrait.Y, portrait.Scale * (portrait.Mirror ? -1 : 1),
                    portrait.Scale, portrait.Alpha, state.SongTime, linear: true);
        }
        if (state.TintAlpha > 0) canvas.Fill(new(0, 0, 320, 180), Color.GameMaker(state.TintColorBgr).Alpha(state.TintAlpha));
        canvas.Flush();
    }
}
