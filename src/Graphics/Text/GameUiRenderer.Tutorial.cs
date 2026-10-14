using KuroakiGimmick.Core;

namespace KuroakiGimmick.Graphics;

public sealed partial class GameUiRenderer
{
    public void DrawTutorialInputs(Session session, double time, NativeGimmickRenderer native)
    {
        if (session.Chart.ObjectName != "obj_tutorial_gimmick") return;
        var inputs = session.Project.NativeInput ?? new NativeInputContext();
        if (inputs.Device == "keyboard")
        {
            Use(session.GameUi);
            if (current?.Data == null) { session.NativeGimmick.Fail("tutorial-font", "Original input-label font is unavailable."); return; }
            string font = Font(session);
            string[] labels = inputs.KeyboardLabels ?? NativeInputContext.DefaultKeys;
            for (int lane = 0; lane < 4; lane++) Text(font, labels[lane].ToUpperInvariant(), 126 + 23 * lane, 150, Color.White, Color.White);
        }
        else
        {
            string sprite = inputs.Device == "playstation" ? "sp_ds_inputs" : "sp_sd_inputs";
            int[] frames = inputs.GamepadIcons ?? [0, 1, 2, 3];
            for (int lane = 0; lane < 4; lane++)
                native.DrawSequenceSprite(session, sprite, frames[lane], 126 + 23 * lane, 150, 1, 1, 1, time);
        }
        canvas.Flush();
    }
}
