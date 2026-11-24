// 分别绘制价格对话的场景元素和 GUI。

using KuroakiGimmick.Core;

namespace KuroakiGimmick.Graphics;

public sealed partial class NativeGimmickRenderer
{
    Session? multigrodeOwner;
    PriceDialogueState? multigrodeState;
    PriceDialogueState Multigrode(Session session)
    {
        if (!ReferenceEquals(multigrodeOwner, session))
        {
            multigrodeOwner = session;
            multigrodeState = new(session.Timeline, session.Chart, session.NativeGimmick.Data!.DialogueTable.Count);
        }
        return multigrodeState!;
    }
    /// <summary>绘制价格对话的场景元素与发送者。</summary>
    /// <param name="session">提供谱面、时间轴及已加载资源的会话。</param>
    /// <param name="time">采样时刻，单位为秒。</param>
    /// <param name="ui">绘制游戏文字和 HUD 的渲染器。</param>
    public void DrawPriceDialogueDialogue(Session session, double time, GameUiRenderer ui)
    {
        if (!session.NativeGimmick.HasBehavior("price-dialogue") || session.NativeGimmick.Data == null) return;
        DrawSequenceSprite(session, "sp_af2024_multi_dialogue", 0, 160, 78, 1, session.Timeline.Get("dialogue_size", time), 1, time);
        ui.DrawPriceDialogueSender(session, time);
    }
    /// <summary>在 GUI 层绘制价格对话及对应文字。</summary>
    /// <param name="session">提供谱面、时间轴及已加载资源的会话。</param>
    /// <param name="time">采样时刻，单位为秒。</param>
    /// <param name="ui">绘制游戏文字和 HUD 的渲染器。</param>
    public void DrawPriceDialogueGui(Session session, double time, GameUiRenderer ui)
    {
        if (!session.NativeGimmick.HasBehavior("price-dialogue") || session.NativeGimmick.Data == null) return;
        var state = Multigrode(session);
        DrawSequenceSprite(session, "sp_af2024_fruitmemory", 0, session.Timeline.Get("fruitmemoryx", time), 35, 1, 1, 1, time);
        ui.DrawPriceDialogueGui(session, state, time);
    }
}
