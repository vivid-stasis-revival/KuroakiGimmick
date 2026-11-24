// 绘制价格对话的发送者与 GUI 文字。

using KuroakiGimmick.Core;

namespace KuroakiGimmick.Graphics;

public sealed partial class GameUiRenderer
{
    /// <summary>绘制当前价格对话的消息发送者文字。</summary>
    /// <param name="session">提供谱面、时间轴及已加载资源的会话。</param>
    /// <param name="time">采样时刻，单位为秒。</param>
    public void DrawPriceDialogueSender(Session session, double time)
    {
        Use(session.GameUi);
        if (current?.Data == null)
        {
            session.NativeGimmick.Fail("multigrode-font", "Original dialogue font is unavailable.");
            return;
        }
        var table = session.NativeGimmick.Data!.DialogueTable;
        if (PriceDialogueState.Index(session.Timeline.Get("dialogue_id", time), table.Count) is not { } index) return;
        var color = Color.White.Alpha(session.Timeline.Get("text_alpha", time));
        Text(Font(session), table[index].Sender + " says:", 35, 49, color, color);
        canvas.Flush();
    }
    /// <summary>绘制价格对话界面的价格与文本元素。</summary>
    /// <param name="session">提供谱面、时间轴及已加载资源的会话。</param>
    /// <param name="state">价格与对话索引的采样状态。</param>
    /// <param name="time">采样时刻，单位为秒。</param>
    public void DrawPriceDialogueGui(Session session, PriceDialogueState state, double time)
    {
        Use(session.GameUi);
        if (current?.Data == null)
        {
            session.NativeGimmick.Fail("multigrode-font", "Original price/dialogue font is unavailable.");
            return;
        }
        string font = Font(session);
        float x = (float)session.Timeline.Get("fruitmemoryx", time);
        double[] prices = [state.Fruits[0], state.Banana, state.Fruits[1], state.Fruits[2], state.Fruits[3]];
        float[] offsets = [51, 51, 44, 51, 31];
        for (int row = 0; row < 5; row++)
            Text(font, "$" + PriceDialogueState.Format(prices[row]), x + offsets[row], 54 + row * 10, Color.White, Color.White);
        var falling = PriceDialogueState.FallingPricePose(session.Timeline, session.Project, time);
        if (falling.Alpha > 0 && double.IsFinite(falling.Y))
        {
            var color = Color.White.Alpha(falling.Alpha);
            for (int lane = 0; lane < 4; lane++)
                Text(font, PriceDialogueState.Format(state.FallingPrice(lane)), 126 + 23 * lane, (float)falling.Y, color, color, center: true, scale: .75f);
        }
        // TextDrawer.gradual 默认关闭，原版没有调用 setGradual 或 next，因此刷新后立即绘出完整文本行。
        if (state.BodyIndexAt(time) is { } body)
        {
            var color = Color.White.Alpha(session.Timeline.Get("text_alpha", time));
            var lines = NativeStoryState.Wrap(session.NativeGimmick.Data!.DialogueTable[body].Text, text => Width(font, text), 248);
            for (int line = 0; line < lines.Count; line++) Text(font, lines[line], 160, 63 + line * 10, color, color, center: true);
        }
        canvas.Flush();
    }
}
