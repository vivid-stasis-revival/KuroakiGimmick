// 绘制插入剧情的 HUD。

using KuroakiGimmick.Core;

namespace KuroakiGimmick.Graphics;

public sealed partial class GameUiRenderer
{
    /// <summary>绘制当前插段所使用的剧情文本界面。</summary>
    /// <param name="session">提供谱面、时间轴及已加载资源的会话。</param>
    /// <param name="state">当前插段的采样状态。</param>
    public void Interlude(Session session, NativeInterludeState state)
    {
        if (state.Resumed) return;
        Use(session.GameUi);
        if (current?.Data == null) return;
        string font = Font(session);
        DrawStoryBox(NativeStoryState.Sample(state.Sequence, state.Age, _ => true, text => Width(font, text), session.Project.NativeStoryContext), font);
    }
}
