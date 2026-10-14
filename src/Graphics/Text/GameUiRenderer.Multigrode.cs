using KuroakiGimmick.Core;

namespace KuroakiGimmick.Graphics;

public sealed partial class GameUiRenderer
{
    public void DrawMultigrodeSender(Session session, double time)
    {
        Use(session.GameUi);
        if (current?.Data == null) { session.NativeGimmick.Fail("multigrode-font", "Original dialogue font is unavailable."); return; }
        var table = session.NativeGimmick.Data!.DialogueTable;
        if (MultigrodeState.Index(session.Timeline.Get("dialogue_id", time), table.Count) is not { } index) return;
        var color = Color.White.Alpha(session.Timeline.Get("text_alpha", time));
        Text(Font(session), table[index].Sender + " says:", 35, 49, color, color);
        canvas.Flush();
    }
    public void DrawMultigrodeGui(Session session, MultigrodeState state, double time)
    {
        Use(session.GameUi);
        if (current?.Data == null) { session.NativeGimmick.Fail("multigrode-font", "Original price/dialogue font is unavailable."); return; }
        string font = Font(session);
        float x = (float)session.Timeline.Get("fruitmemoryx", time);
        double[] prices = [state.Fruits[0], state.Banana, state.Fruits[1], state.Fruits[2], state.Fruits[3]];
        float[] offsets = [51, 51, 44, 51, 31];
        for (int row = 0; row < 5; row++)
            Text(font, "$" + MultigrodeState.Format(prices[row]), x + offsets[row], 54 + row * 10, Color.White, Color.White);
        var falling = MultigrodeState.FallingPricePose(session.Timeline, session.Project, time);
        if (falling.Alpha > 0 && double.IsFinite(falling.Y))
        {
            var color = Color.White.Alpha(falling.Alpha);
            for (int lane = 0; lane < 4; lane++)
                Text(font, MultigrodeState.Format(state.FallingPrice(lane)), 126 + 23 * lane, (float)falling.Y, color, color, center: true, scale: .75f);
        }
        // TextDrawer.gradual defaults to false. The source never calls setGradual
        // or next(), so refresh renders complete lines immediately, not a typewriter.
        if (state.BodyIndexAt(time) is { } body)
        {
            var color = Color.White.Alpha(session.Timeline.Get("text_alpha", time));
            var lines = NativeStoryState.Wrap(session.NativeGimmick.Data!.DialogueTable[body].Text, text => Width(font, text), 248);
            for (int line = 0; line < lines.Count; line++) Text(font, lines[line], 160, 63 + line * 10, color, color, center: true);
        }
        canvas.Flush();
    }
}
